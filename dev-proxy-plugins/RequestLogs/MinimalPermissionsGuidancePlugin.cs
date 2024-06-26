// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs.MinimalPermissions;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;

namespace Microsoft.DevProxy.Plugins.RequestLogs;

public class MinimalPermissionsGuidancePluginReport
{
    public MinimalPermissionsInfo? DelegatedPermissions { get; set; }
    public MinimalPermissionsInfo? ApplicationPermissions { get; set; }
}

public class OperationInfo
{
    public string Method { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}

public class MinimalPermissionsInfo
{
    public string[] MinimalPermissions { get; set; } = Array.Empty<string>();
    public string[] PermissionsFromTheToken { get; set; } = Array.Empty<string>();
    public string[] ExcessPermissions { get; set; } = Array.Empty<string>();
    public OperationInfo[] Operations { get; set; } = Array.Empty<OperationInfo>();
}

public class MinimalPermissionsGuidancePlugin : BaseReportingPlugin
{
    public override string Name => nameof(MinimalPermissionsGuidancePlugin);

    public MinimalPermissionsGuidancePlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override void Register()
    {
        base.Register();

        PluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    private async Task AfterRecordingStop(object? sender, RecordingArgs e)
    {
        if (!e.RequestLogs.Any())
        {
            return;
        }

        var methodAndUrlComparer = new MethodAndUrlComparer();
        var delegatedEndpoints = new List<(string method, string url)>();
        var applicationEndpoints = new List<(string method, string url)>();

        // scope for delegated permissions
        var scopesToEvaluate = Array.Empty<string>();
        // roles for application permissions
        var rolesToEvaluate = Array.Empty<string>();

        foreach (var request in e.RequestLogs)
        {
            if (request.MessageType != MessageType.InterceptedRequest)
            {
                continue;
            }

            var methodAndUrlString = request.MessageLines.First();
            var methodAndUrl = GetMethodAndUrl(methodAndUrlString);
            if (methodAndUrl.method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            var requestsFromBatch = Array.Empty<(string method, string url)>();

            var uri = new Uri(methodAndUrl.url);
            if (!ProxyUtils.IsGraphUrl(uri))
            {
                continue;
            }

            if (ProxyUtils.IsGraphBatchUrl(uri))
            {
                var graphVersion = ProxyUtils.IsGraphBetaUrl(uri) ? "beta" : "v1.0";
                requestsFromBatch = GetRequestsFromBatch(request.Context?.Session.HttpClient.Request.BodyString!, graphVersion, uri.Host);
            }
            else
            {
                methodAndUrl = (methodAndUrl.method, GetTokenizedUrl(methodAndUrl.url));
            }

            var scopesAndType = GetPermissionsAndType(request);
            if (scopesAndType.type == PermissionsType.Delegated)
            {
                // use the scopes from the last request in case the app is using incremental consent
                scopesToEvaluate = scopesAndType.permissions;

                if (ProxyUtils.IsGraphBatchUrl(uri))
                {
                    delegatedEndpoints.AddRange(requestsFromBatch);
                }
                else
                {
                    delegatedEndpoints.Add(methodAndUrl);
                }
            }
            else
            {
                // skip empty roles which are returned in case we couldn't get permissions information
                // 
                // application permissions are always the same because they come from app reg
                // so we can just use the first request that has them
                if (scopesAndType.permissions.Length > 0 &&
                  rolesToEvaluate.Length == 0)
                {
                    rolesToEvaluate = scopesAndType.permissions;

                    if (ProxyUtils.IsGraphBatchUrl(uri))
                    {
                        applicationEndpoints.AddRange(requestsFromBatch);
                    }
                    else
                    {
                        applicationEndpoints.Add(methodAndUrl);
                    }
                }
            }
        }

        // Remove duplicates
        delegatedEndpoints = delegatedEndpoints.Distinct(methodAndUrlComparer).ToList();
        applicationEndpoints = applicationEndpoints.Distinct(methodAndUrlComparer).ToList();

        if (delegatedEndpoints.Count == 0 && applicationEndpoints.Count == 0)
        {
            return;
        }

        var report = new MinimalPermissionsGuidancePluginReport();

        Logger.LogWarning("This plugin is in preview and may not return the correct results.\r\nPlease review the permissions and test your app before using them in production.\r\nIf you have any feedback, please open an issue at https://aka.ms/devproxy/issue.\r\n");

        if (delegatedEndpoints.Count > 0)
        {
            var delegatedPermissionsInfo = new MinimalPermissionsInfo();
            report.DelegatedPermissions = delegatedPermissionsInfo;

            Logger.LogInformation("Evaluating delegated permissions for:\r\n{endpoints}\r\n", string.Join(Environment.NewLine, delegatedEndpoints.Select(e => $"- {e.method} {e.url}")));

            await EvaluateMinimalScopes(delegatedEndpoints, scopesToEvaluate, PermissionsType.Delegated, delegatedPermissionsInfo);
        }

        if (applicationEndpoints.Count > 0)
        {
            var applicationPermissionsInfo = new MinimalPermissionsInfo();
            report.ApplicationPermissions = applicationPermissionsInfo;

            Logger.LogInformation("Evaluating application permissions for:\r\n{applicationPermissions}\r\n", string.Join(Environment.NewLine, applicationEndpoints.Select(e => $"- {e.method} {e.url}")));

            await EvaluateMinimalScopes(applicationEndpoints, rolesToEvaluate, PermissionsType.Application, applicationPermissionsInfo);
        }

        StoreReport(report, e);
    }

    private (string method, string url)[] GetRequestsFromBatch(string batchBody, string graphVersion, string graphHostName)
    {
        var requests = new List<(string method, string url)>();

        if (string.IsNullOrEmpty(batchBody))
        {
            return requests.ToArray();
        }

        try
        {
            var batch = JsonSerializer.Deserialize<GraphBatchRequestPayload>(batchBody, ProxyUtils.JsonSerializerOptions);
            if (batch == null)
            {
                return requests.ToArray();
            }

            foreach (var request in batch.Requests)
            {
                try
                {
                    var method = request.Method;
                    var url = request.Url;
                    var absoluteUrl = $"https://{graphHostName}/{graphVersion}{url}";
                    requests.Add((method, GetTokenizedUrl(absoluteUrl)));
                }
                catch { }
            }
        }
        catch { }

        return requests.ToArray();
    }

    /// <summary>
    /// Returns permissions and type (delegated or application) from the access token
    /// used on the request.
    /// If it can't get the permissions, returns PermissionType.Application
    /// and an empty array
    /// </summary>
    private (PermissionsType type, string[] permissions) GetPermissionsAndType(RequestLog request)
    {
        var authHeader = request.Context?.Session.HttpClient.Request.Headers.GetFirstHeader("Authorization");
        if (authHeader == null)
        {
            return (PermissionsType.Application, Array.Empty<string>());
        }

        var token = authHeader.Value.Replace("Bearer ", string.Empty);
        var tokenChunks = token.Split('.');
        if (tokenChunks.Length != 3)
        {
            return (PermissionsType.Application, Array.Empty<string>());
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtSecurityToken = handler.ReadJwtToken(token);

            var scopeClaim = jwtSecurityToken.Claims.FirstOrDefault(c => c.Type == "scp");
            if (scopeClaim == null)
            {
                // possibly an application token
                // roles is an array so we need to handle it differently
                var roles = jwtSecurityToken.Claims
                  .Where(c => c.Type == "roles")
                  .Select(c => c.Value)
                  .ToArray();
                if (roles.Length == 0)
                {
                    return (PermissionsType.Application, Array.Empty<string>());
                }
                else
                {
                    return (PermissionsType.Application, roles);
                }
            }
            else
            {
                return (PermissionsType.Delegated, scopeClaim.Value.Split(' '));
            }
        }
        catch
        {
            return (PermissionsType.Application, Array.Empty<string>());
        }
    }

    private async Task EvaluateMinimalScopes(IEnumerable<(string method, string url)> endpoints, string[] permissionsFromAccessToken, PermissionsType scopeType, MinimalPermissionsInfo permissionsInfo)
    {
        var payload = endpoints.Select(e => new RequestInfo { Method = e.method, Url = e.url });

        permissionsInfo.Operations = endpoints.Select(e => new OperationInfo
        {
            Method = e.method,
            Endpoint = e.url
        }).ToArray();
        permissionsInfo.PermissionsFromTheToken = permissionsFromAccessToken;

        try
        {
            var url = $"https://graphexplorerapi.azurewebsites.net/permissions?scopeType={GraphUtils.GetScopeTypeString(scopeType)}";
            using var client = new HttpClient();
            var stringPayload = JsonSerializer.Serialize(payload, ProxyUtils.JsonSerializerOptions);
            Logger.LogDebug(string.Format("Calling {0} with payload{1}{2}", url, Environment.NewLine, stringPayload));

            var response = await client.PostAsJsonAsync(url, payload);
            var content = await response.Content.ReadAsStringAsync();

            Logger.LogDebug(string.Format("Response:{0}{1}", Environment.NewLine, content));

            var resultsAndErrors = JsonSerializer.Deserialize<ResultsAndErrors>(content, ProxyUtils.JsonSerializerOptions);
            var minimalPermissions = resultsAndErrors?.Results?.Select(p => p.Value).ToArray() ?? Array.Empty<string>();
            var errors = resultsAndErrors?.Errors?.Select(e => $"- {e.Url} ({e.Message})") ?? Array.Empty<string>();

            if (scopeType == PermissionsType.Delegated)
            {
                minimalPermissions = await GraphUtils.UpdateUserScopes(minimalPermissions, endpoints, scopeType, Logger);
            }

            if (minimalPermissions.Any())
            {
                var excessPermissions = permissionsFromAccessToken
                  .Where(p => !minimalPermissions.Contains(p))
                  .ToArray();

                permissionsInfo.MinimalPermissions = minimalPermissions;
                permissionsInfo.ExcessPermissions = excessPermissions;

                Logger.LogInformation("Minimal permissions: {minimalPermissions}", string.Join(", ", minimalPermissions));
                Logger.LogInformation("Permissions on the token: {tokenPermissions}", string.Join(", ", permissionsFromAccessToken));

                if (excessPermissions.Any())
                {
                    Logger.LogWarning("The following permissions are unnecessary: {permissions}", string.Join(", ", excessPermissions));
                }
                else
                {
                    Logger.LogInformation("The token has the minimal permissions required.");
                }
            }
            if (errors.Any())
            {
                Logger.LogError("Couldn't determine minimal permissions for the following URLs: {errors}", string.Join(", ", errors));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error has occurred while retrieving minimal permissions: {message}", ex.Message);
        }
    }

    private (string method, string url) GetMethodAndUrl(string message)
    {
        var info = message.Split(" ");
        if (info.Length > 2)
        {
            info = [info[0], string.Join(" ", info.Skip(1))];
        }
        return (method: info[0], url: info[1]);
    }

    private string GetTokenizedUrl(string absoluteUrl)
    {
        var sanitizedUrl = ProxyUtils.SanitizeUrl(absoluteUrl);
        return "/" + string.Join("", new Uri(sanitizedUrl).Segments.Skip(2).Select(Uri.UnescapeDataString));
    }
}
