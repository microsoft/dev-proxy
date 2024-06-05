// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs.ApiCenter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Microsoft.DevProxy.Plugins.RequestLogs;

public class ApiCenterMinimalPermissionsPluginReportApiResult
{
    public required string ApiId { get; init; }
    public required string ApiName { get; init; }
    public required string ApiDefinitionId { get; init; }
    public required string[] Requests { get; init; }
    public required string[] TokenPermissions { get; init; }
    public required string[] MinimalPermissions { get; init; }
    public required string[] ExcessivePermissions { get; init; }
    public required bool UsesMinimalPermissions { get; init; }
}

public class ApiCenterMinimalPermissionsPluginReportError
{
    public required string Request { get; init; }
    public required string Error { get; init; }
}

public class ApiCenterMinimalPermissionsPluginReport
{
    public required ApiCenterMinimalPermissionsPluginReportApiResult[] Results { get; init; }
    public required string[] UnmatchedRequests { get; init; }
    public required ApiCenterMinimalPermissionsPluginReportError[] Errors { get; init; }
}

internal class ApiCenterMinimalPermissionsPluginConfiguration
{
    public string SubscriptionId { get; set; } = "";
    public string ResourceGroupName { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string WorkspaceName { get; set; } = "default";
}

public class ApiCenterMinimalPermissionsPlugin : BaseReportingPlugin
{
    private ApiCenterProductionVersionPluginConfiguration _configuration = new();
    private ApiCenterClient? _apiCenterClient;
    private Api[]? _apis;
    private Dictionary<string, ApiDefinition>? _apiDefinitionsByUrl;

    public ApiCenterMinimalPermissionsPlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override string Name => nameof(ApiCenterMinimalPermissionsPlugin);

    public override void Register()
    {
        base.Register();

        ConfigSection?.Bind(_configuration);

        try
        {
            _apiCenterClient = new(
                new()
                {
                    SubscriptionId = _configuration.SubscriptionId,
                    ResourceGroupName = _configuration.ResourceGroupName,
                    ServiceName = _configuration.ServiceName,
                    WorkspaceName = _configuration.WorkspaceName
                },
                Logger
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create API Center client. The {plugin} will not be used.", Name);
            return;
        }

        Logger.LogInformation("Plugin {plugin} connecting to Azure...", Name);
        try
        {
            _ = _apiCenterClient.GetAccessToken(CancellationToken.None).Result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to authenticate with Azure. The {plugin} will not be used.", Name);
            return;
        }
        Logger.LogDebug("Plugin {plugin} auth confirmed...", Name);

        PluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    private async Task AfterRecordingStop(object sender, RecordingArgs e)
    {
        var interceptedRequests = e.RequestLogs
            .Where(l =>
                l.MessageType == MessageType.InterceptedRequest &&
                !l.MessageLines.First().StartsWith("OPTIONS") &&
                l.Context?.Session is not null &&
                l.Context.Session.HttpClient.Request.Headers.Any(h => h.Name.Equals("authorization", StringComparison.OrdinalIgnoreCase))
            );
        if (!interceptedRequests.Any())
        {
            Logger.LogDebug("No requests to process");
            return;
        }

        Logger.LogInformation("Checking if recorded API requests use minimal permissions as defined in API Center...");

        Debug.Assert(_apiCenterClient is not null);

        if (_apis is null)
        {
            _apis = await _apiCenterClient.GetApis();
        }

        if (_apis is null || !_apis.Any())
        {
            Logger.LogInformation("No APIs found in API Center");
            return;
        }

        // get all API definitions by URL so that we can easily match
        // API requests to API definitions, for permissions lookup
        if (_apiDefinitionsByUrl is null)
        {
            _apiDefinitionsByUrl = await _apis.GetApiDefinitionsByUrl(_apiCenterClient, Logger);
        }

        var (requestsByApiDefinition, unmatchedApicRequests) = GetRequestsByApiDefinition(interceptedRequests, _apiDefinitionsByUrl);

        var errors = new List<ApiCenterMinimalPermissionsPluginReportError>();
        var results = new List<ApiCenterMinimalPermissionsPluginReportApiResult>();
        var unmatchedRequests = new List<string>(
            unmatchedApicRequests.Select(r => r.MessageLines.First())
        );
        
        foreach (var (apiDefinition, requests) in requestsByApiDefinition)
        {
            var (
                tokenPermissions,
                operationsFromRequests,
                minimalScopes,
                unmatchedOperations,
                errorsForApi
            ) = CheckMinimalPermissions(requests, apiDefinition);

            var api = _apis.FindApiByDefinition(apiDefinition, Logger);
            var result = new ApiCenterMinimalPermissionsPluginReportApiResult
            {
                ApiId = api?.Id ?? "unknown",
                ApiName = api?.Properties?.Title ?? "unknown",
                ApiDefinitionId = apiDefinition.Id!,
                Requests = operationsFromRequests
                    .Select(o => $"{o.method} {o.originalUrl}")
                    .Distinct()
                    .ToArray(),
                TokenPermissions = tokenPermissions.Distinct().ToArray(),
                MinimalPermissions = minimalScopes,
                ExcessivePermissions = tokenPermissions.Except(minimalScopes).ToArray(),
                UsesMinimalPermissions = !tokenPermissions.Except(minimalScopes).Any()
            };
            results.Add(result);

            var unmatchedApiRequests = operationsFromRequests
                .Where(o => unmatchedOperations.Contains($"{o.method} {o.tokenizedUrl}"))
                .Select(o => $"{o.method} {o.originalUrl}");
            unmatchedRequests.AddRange(unmatchedApiRequests);
            errors.AddRange(errorsForApi);

            if (result.UsesMinimalPermissions)
            {
                Logger.LogInformation(
                    "API {apiName} is called with minimal permissions: {minimalPermissions}",
                    result.ApiName,
                    string.Join(", ", result.MinimalPermissions)
                );
            }
            else
            {
                Logger.LogWarning(
                    "Calling API {apiName} with excessive permissions: {excessivePermissions}. Minimal permissions are: {minimalPermissions}",
                    result.ApiName,
                    string.Join(", ", result.ExcessivePermissions),
                    string.Join(", ", result.MinimalPermissions)
                );
            }

            if (unmatchedApiRequests.Any())
            {
                Logger.LogWarning(
                    "Unmatched requests for API {apiName}:{newLine}- {unmatchedRequests}",
                    result.ApiName,
                    Environment.NewLine,
                    string.Join($"{Environment.NewLine}- ", unmatchedApiRequests)
                );
            }

            if (errorsForApi.Any())
            {
                Logger.LogWarning(
                    "Errors for API {apiName}:{newLine}- {errors}",
                    result.ApiName,
                    Environment.NewLine,
                    string.Join($"{Environment.NewLine}- ", errorsForApi.Select(e => $"{e.Request}: {e.Error}"))
                );
            }
        }

        var report = new ApiCenterMinimalPermissionsPluginReport()
        {
            Results = results.ToArray(),
            UnmatchedRequests = unmatchedRequests.ToArray(),
            Errors = errors.ToArray()
        };

        StoreReport(report, e);
    }

    private (
        List<string> tokenPermissions,
        List<(string method, string originalUrl, string tokenizedUrl)> operationsFromRequests,
        string[] minimalScopes,
        string[] unmatchedOperations,
        List<ApiCenterMinimalPermissionsPluginReportError> errors
        ) CheckMinimalPermissions(IEnumerable<RequestLog> requests, ApiDefinition apiDefinition)
    {
        Logger.LogInformation("Checking minimal permissions for API {apiName}...", apiDefinition.Definition!.Servers.First().Url);

        var tokenPermissions = new List<string>();
        var operationsFromRequests = new List<(string method, string originalUrl, string tokenizedUrl)>();
        var operationsAndScopes = new Dictionary<string, string[]>();
        var errors = new List<ApiCenterMinimalPermissionsPluginReportError>();

        foreach (var request in requests)
        {
            // get scopes from the token
            var methodAndUrl = request.MessageLines.First();
            var methodAndUrlChunks = methodAndUrl.Split(' ');
            Logger.LogDebug("Checking request {request}...", methodAndUrl);
            var (method, url) = (methodAndUrlChunks[0].ToUpper(), methodAndUrlChunks[1]);

            var scopesFromTheToken = GetScopesFromToken(request.Context?.Session.HttpClient.Request.Headers.First(h => h.Name.Equals("authorization", StringComparison.OrdinalIgnoreCase)).Value);
            if (scopesFromTheToken.Any())
            {
                tokenPermissions.AddRange(scopesFromTheToken);
            }
            else
            {
                errors.Add(new()
                {
                    Request = methodAndUrl,
                    Error = "No scopes found in the token"
                });
            }

            // get allowed scopes for the operation
            if (!Enum.TryParse<OperationType>(method, true, out var operationType))
            {
                errors.Add(new()
                {
                    Request = methodAndUrl,
                    Error = $"{method} is not a valid HTTP method"
                });
                continue;
            }

            var pathItem = apiDefinition.Definition!.FindMatchingPathItem(url, Logger);
            if (pathItem is null)
            {
                errors.Add(new()
                {
                    Request = methodAndUrl,
                    Error = "No matching path item found"
                });
                continue;
            }

            if (!pathItem.Value.Value.Operations.TryGetValue(operationType, out var operation))
            {
                errors.Add(new()
                {
                    Request = methodAndUrl,
                    Error = "No matching operation found"
                });
                continue;
            }

            var scopes = operation.GetEffectiveScopes(apiDefinition.Definition!, Logger);
            if (scopes.Any())
            {
                operationsAndScopes[$"{method} {pathItem.Value.Key}"] = scopes;
            }

            operationsFromRequests.Add((operationType.ToString().ToUpper(), url, pathItem.Value.Key));
        }

        var (minimalScopes, unmatchedOperations) = GetMinimalScopes(
            operationsFromRequests
                .Select(o => $"{o.method} {o.tokenizedUrl}")
                .Distinct()
                .ToArray(),
            operationsAndScopes
        );

        return (tokenPermissions, operationsFromRequests, minimalScopes, unmatchedOperations, errors);
    }

    /// <summary>
    /// Gets the scopes from the JWT token.
    /// </summary>
    /// <param name="jwtToken">The JWT token including the 'Bearer' prefix.</param>
    /// <returns>The scopes from the JWT token or empty array if no scopes found or error occurred.</returns>
    private string[] GetScopesFromToken(string? jwtToken)
    {
        Logger.LogDebug("Getting scopes from JWT token...");

        if (string.IsNullOrEmpty(jwtToken))
        {
            return [];
        }

        try
        {
            var token = jwtToken.Split(' ')[1];
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;
            var scopes = jsonToken?.Claims
                .Where(c => c.Type == "scp")
                .Select(c => c.Value)
                .ToArray() ?? [];

            Logger.LogDebug("Scopes found in the token: {scopes}", string.Join(", ", scopes));
            return scopes;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse JWT token");
            return [];
        }
    }

    private (Dictionary<ApiDefinition, List<RequestLog>> RequestsByApiDefinition, IEnumerable<RequestLog> UnmatchedRequests) GetRequestsByApiDefinition(IEnumerable<RequestLog> interceptedRequests, Dictionary<string, ApiDefinition> apiDefinitionsByUrl)
    {
        var unmatchedRequests = new List<RequestLog>();
        var requestsByApiDefinition = new Dictionary<ApiDefinition, List<RequestLog>>();
        foreach (var request in interceptedRequests)
        {
            var url = request.MessageLines.First().Split(' ')[1];
            Logger.LogDebug("Matching request {requestUrl} to API definitions...", url);

            var matchingKey = apiDefinitionsByUrl.Keys.FirstOrDefault(url.StartsWith);
            if (matchingKey is null)
            {
                Logger.LogDebug("No matching API definition found for {requestUrl}", url);
                unmatchedRequests.Add(request);
                continue;
            }

            if (!requestsByApiDefinition.ContainsKey(apiDefinitionsByUrl[matchingKey]))
            {
                requestsByApiDefinition[apiDefinitionsByUrl[matchingKey]] = new();
            }
            requestsByApiDefinition[apiDefinitionsByUrl[matchingKey]].Add(request);
        }

        return (requestsByApiDefinition, unmatchedRequests);
    }

    (string[] minimalScopes, string[] unmatchedOperations) GetMinimalScopes(string[] requests, Dictionary<string, string[]> operationsAndScopes)
    {
        var unmatchedOperations = requests
            .Where(o => !operationsAndScopes.Keys.Contains(o, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var minimalScopesPerOperation = operationsAndScopes
            .Where(o => requests.Contains(o.Key, StringComparer.OrdinalIgnoreCase))
            .Select(o => new KeyValuePair<string, string>(o.Key, o.Value.First()))
            .ToDictionary();

        // for each minimal scope check if it overrules any other minimal scope
        // (position > 0, because the minimal scope is always first). if it does,
        // replace the minimal scope with the overruling scope
        foreach (var scope in minimalScopesPerOperation.Values)
        {
            foreach (var minimalScope in minimalScopesPerOperation)
            {
                if (Array.IndexOf(operationsAndScopes[minimalScope.Key], scope) > 0)
                {
                    minimalScopesPerOperation[minimalScope.Key] = scope;
                }
            }
        }

        return (
            minimalScopesPerOperation
                .Select(s => s.Value)
                .Distinct()
                .ToArray(),
            unmatchedOperations
        );
    }
}