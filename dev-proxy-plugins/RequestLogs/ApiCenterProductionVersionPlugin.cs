// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Diagnostics;
using Azure.Identity;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins;
using Microsoft.DevProxy.Plugins.RequestLogs.ApiCenter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Readers;

public enum ApiCenterProductionVersionPluginReportItemStatus
{
    NotRegistered,
    NonProduction,
    Production
}

public class ApiCenterProductionVersionPluginReportItem
{
    public required string Method { get; init; }
    public required string Url { get; init; }
    public required ApiCenterProductionVersionPluginReportItemStatus Status { get; init; }
    public string? Recommendation { get; init; }
}

public class ApiCenterProductionVersionPluginReport: List<ApiCenterProductionVersionPluginReportItem>;

internal class ApiInformation
{
    public string Name { get; set; } = "";
    public ApiInformationVersion[] Versions { get; set; } = [];
}

internal class ApiInformationVersion
{
    public string Title { get; set; } = "";
    public string Name { get; set; } = "";
    public ApiLifecycleStage? LifecycleStage { get; set; }
    public string[] Urls { get; set; } = [];
}

internal class ApiCenterProductionVersionPluginConfiguration
{
    public string SubscriptionId { get; set; } = "";
    public string ResourceGroupName { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string WorkspaceName { get; set; } = "default";
}

public class ApiCenterProductionVersionPlugin : BaseProxyPlugin
{
    private ApiCenterProductionVersionPluginConfiguration _configuration = new();
    private readonly string[] _scopes = ["https://management.azure.com/.default"];
    private TokenCredential _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions() {
        ExcludeInteractiveBrowserCredential = true,
        // fails on Ubuntu
        ExcludeSharedTokenCacheCredential = true
    });
    private HttpClient? _httpClient;
    private JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override string Name => nameof(ApiCenterProductionVersionPlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        configSection?.Bind(_configuration);

        if (string.IsNullOrEmpty(_configuration.SubscriptionId))
        {
            _logger?.LogError("Specify SubscriptionId in the ApiCenterProductionVersionPlugin configuration. The ApiCenterProductionVersionPlugin will not be used.");
            return;
        }
        if (string.IsNullOrEmpty(_configuration.ResourceGroupName))
        {
            _logger?.LogError("Specify ResourceGroupName in the ApiCenterProductionVersionPlugin configuration. The ApiCenterProductionVersionPlugin will not be used.");
            return;
        }
        if (string.IsNullOrEmpty(_configuration.ServiceName))
        {
            _logger?.LogError("Specify ServiceName in the ApiCenterProductionVersionPlugin configuration. The ApiCenterProductionVersionPlugin will not be used.");
            return;
        }

        // load configuration from env vars
        if (_configuration.SubscriptionId.StartsWith('@'))
        {
            _configuration.SubscriptionId = Environment.GetEnvironmentVariable(_configuration.SubscriptionId.Substring(1)) ?? _configuration.SubscriptionId;
        }
        if (_configuration.ResourceGroupName.StartsWith('@'))
        {
            _configuration.ResourceGroupName = Environment.GetEnvironmentVariable(_configuration.ResourceGroupName.Substring(1)) ?? _configuration.ResourceGroupName;
        }
        if (_configuration.ServiceName.StartsWith('@'))
        {
            _configuration.ServiceName = Environment.GetEnvironmentVariable(_configuration.ServiceName.Substring(1)) ?? _configuration.ServiceName;
        }
        if (_configuration.WorkspaceName.StartsWith('@'))
        {
            _configuration.WorkspaceName = Environment.GetEnvironmentVariable(_configuration.WorkspaceName.Substring(1)) ?? _configuration.WorkspaceName;
        }

        if (_logger?.LogLevel == LogLevel.Debug)
        {
            var consoleListener = AzureEventSourceListener.CreateConsoleLogger(EventLevel.Verbose);
        }

        var authenticationHandler = new AuthenticationDelegatingHandler(_credential, _scopes)
        {
            InnerHandler = new HttpClientHandler()
        };
        
        _logger?.LogDebug("Plugin {plugin} checking Azure auth...", Name);
        
        try
        {
            _ = authenticationHandler.GetAccessToken(CancellationToken.None).Result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to authenticate with Azure. The {plugin} will not be used.", Name);
            return;
        }
        _logger?.LogDebug("Plugin {plugin} auth confirmed...", Name);

        _httpClient = new HttpClient(authenticationHandler);

        pluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    private async Task AfterRecordingStop(object sender, RecordingArgs e)
    {
        var interceptedRequests = e.RequestLogs
            .Where(
                l => l.MessageType == MessageType.InterceptedRequest &&
                l.Context?.Session is not null
            );
        if (!interceptedRequests.Any())
        {
            _logger?.LogDebug("No requests to process");
            return;
        }

        _logger?.LogInformation("Checking if recorded API requests use production APIs as defined in API Center...");

        Debug.Assert(_httpClient is not null);

        var apisFromApiCenter = await LoadApisFromApiCenter();
        if (apisFromApiCenter == null || !apisFromApiCenter.Value.Any())
        {
            _logger?.LogInformation("No APIs found in API Center");
            return;
        }

        var apisInformation = new List<ApiInformation>();
        foreach (var api in apisFromApiCenter.Value)
        {
            var apiVersionsFromApiCenter = await LoadApiVersionsFromApiCenter(api);
            if (apiVersionsFromApiCenter == null || !apiVersionsFromApiCenter.Value.Any())
            {
                _logger?.LogInformation("No versions found for {api}", api.Properties?.Title);
                continue;
            }

            var versions = new List<ApiInformationVersion>();
            foreach (var versionFromApiCenter in apiVersionsFromApiCenter.Value)
            {
                Debug.Assert(versionFromApiCenter.Id is not null);

                var definitionsFromApiCenter = await LoadApiDefinitionsForVersion(versionFromApiCenter.Id);
                if (definitionsFromApiCenter is null || !definitionsFromApiCenter.Value.Any())
                {
                    _logger?.LogDebug("No definitions found for version {versionId}", versionFromApiCenter.Id);
                    continue;
                }

                var apiUrls = new HashSet<string>();
                foreach (var definitionFromApiCenter in definitionsFromApiCenter.Value)
                {
                    Debug.Assert(definitionFromApiCenter.Id is not null);

                    await EnsureApiDefinition(definitionFromApiCenter);

                    if (definitionFromApiCenter.Definition is null)
                    {
                        _logger?.LogDebug("API definition not found for {definitionId}", definitionFromApiCenter.Id);
                        continue;
                    }

                    if (!definitionFromApiCenter.Definition.Servers.Any())
                    {
                        _logger?.LogDebug("No servers found for API definition {definitionId}", definitionFromApiCenter.Id);
                        continue;
                    }

                    foreach (var server in definitionFromApiCenter.Definition.Servers)
                    {
                        apiUrls.Add(server.Url);
                    }
                }

                if (!apiUrls.Any())
                {
                    _logger?.LogDebug("No URLs found for version {versionId}", versionFromApiCenter.Id);
                    continue;
                }

                versions.Add(new ApiInformationVersion
                {
                    Title = versionFromApiCenter.Properties?.Title ?? "",
                    Name = versionFromApiCenter.Name ?? "",
                    LifecycleStage = versionFromApiCenter.Properties?.LifecycleStage,
                    Urls = apiUrls.ToArray()
                });
            }

            if (!versions.Any())
            {
                _logger?.LogInformation("No versions found for {api}", api.Properties?.Title);
                continue;
            }

            apisInformation.Add(new ApiInformation
            {
                Name = api.Properties?.Title ?? "",
                Versions = versions.ToArray()
            });
        }

        _logger?.LogInformation("Analyzing recorded requests...");

        var report = new ApiCenterProductionVersionPluginReport();

        foreach (var request in interceptedRequests)
        {
            var methodAndUrlString = request.MessageLines.First();
            var methodAndUrl = methodAndUrlString.Split(' ');
            if (methodAndUrl[0] == "OPTIONS")
            {
                _logger?.LogDebug("Skipping OPTIONS request {request}", methodAndUrl[1]);
                continue;
            }

            var apiInformation = FindMatchingApiInformation(methodAndUrl[1], apisInformation);
            if (apiInformation == null)
            {
                report.Add(new()
                {
                    Method = methodAndUrl[0],
                    Url = methodAndUrl[1],
                    Status = ApiCenterProductionVersionPluginReportItemStatus.NotRegistered
                });
                continue;
            }

            var lifecycleStage = FindMatchingApiLifecycleStage(request, methodAndUrl[1], apiInformation);
            if (lifecycleStage == null)
            {
                report.Add(new()
                {
                    Method = methodAndUrl[0],
                    Url = methodAndUrl[1],
                    Status = ApiCenterProductionVersionPluginReportItemStatus.NotRegistered
                });
                continue;
            }

            if (lifecycleStage != ApiLifecycleStage.Production)
            {
                var productionVersions = apiInformation.Versions
                    .Where(v => v.LifecycleStage == ApiLifecycleStage.Production)
                    .Select(v => v.Title);

                var recommendation = productionVersions.Any() ?
                    string.Format("Request {0} uses API version {1} which is defined as {2}. Upgrade to a production version of the API. Recommended versions: {3}", methodAndUrlString, apiInformation.Versions.First(v => v.LifecycleStage == lifecycleStage).Title, lifecycleStage, string.Join(", ", productionVersions)) :
                    string.Format("Request {0} uses API version {1} which is defined as {2}.", methodAndUrlString, apiInformation.Versions.First(v => v.LifecycleStage == lifecycleStage).Title, lifecycleStage);

                report.Add(new()
                {
                    Method = methodAndUrl[0],
                    Url = methodAndUrl[1],
                    Status = ApiCenterProductionVersionPluginReportItemStatus.NonProduction,
                    Recommendation = recommendation
                });
            }
            else
            {
                report.Add(new()
                {
                    Method = methodAndUrl[0],
                    Url = methodAndUrl[1],
                    Status = ApiCenterProductionVersionPluginReportItemStatus.Production
                });
            }
        }

        _logger?.LogDebug("DONE");
    }

    private async Task<Collection<ApiDefinition>?> LoadApiDefinitionsForVersion(string versionId)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogDebug("Loading API definitions for version {id}...", versionId);

        var res = await _httpClient.GetStringAsync($"https://management.azure.com{versionId}/definitions?api-version=2024-03-01");
        return JsonSerializer.Deserialize<Collection<ApiDefinition>>(res, _jsonSerializerOptions);
    }

    async Task EnsureApiDefinition(ApiDefinition apiDefinition)
    {
        Debug.Assert(_httpClient is not null);

        if (apiDefinition.Definition is not null)
        {
            _logger?.LogDebug("API definition already loaded for {apiDefinitionId}", apiDefinition.Id);
            return;
        }

        _logger?.LogDebug("Loading API definition for {apiDefinitionId}...", apiDefinition.Id);

        var res = await _httpClient.GetStringAsync($"https://management.azure.com{apiDefinition.Id}?api-version=2024-03-01");
        var definition = JsonSerializer.Deserialize<ApiDefinition>(res, _jsonSerializerOptions);
        if (definition is null)
        {
            _logger?.LogError("Failed to deserialize API definition for {apiDefinitionId}", apiDefinition.Id);
            return;
        }

        apiDefinition.Properties = definition.Properties;
        if (apiDefinition.Properties?.Specification?.Name != "openapi")
        {
            _logger?.LogDebug("API definition is not OpenAPI for {apiDefinitionId}", apiDefinition.Id);
            return;
        }

        var definitionRes = await _httpClient.PostAsync($"https://management.azure.com{apiDefinition.Id}/exportSpecification?api-version=2024-03-01", null);
        var exportResult = await definitionRes.Content.ReadFromJsonAsync<ApiSpecExportResult>();
        if (exportResult is null)
        {
            _logger?.LogError("Failed to deserialize exported API definition for {apiDefinitionId}", apiDefinition.Id);
            return;
        }

        if (exportResult.Format != ApiSpecExportResultFormat.Inline)
        {
            _logger?.LogDebug("API definition is not inline for {apiDefinitionId}", apiDefinition.Id);
            return;
        }

        try
        {
            apiDefinition.Definition = new OpenApiStringReader().Read(exportResult.Value, out _);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse OpenAPI document for {apiDefinitionId}", apiDefinition.Id);
            return;
        }
    }

    private ApiInformation? FindMatchingApiInformation(string requestUrl, List<ApiInformation>? apisInformation)
    {
        var apiInformation = apisInformation?.FirstOrDefault(a => a.Versions.Any(v => v.Urls.Any(u => requestUrl.StartsWith(u))));
        if (apiInformation is null)
        {
            _logger?.LogDebug("No matching API found for {request}", requestUrl);
        }
        else
        {
            _logger?.LogDebug("Found matching API information for {request}", requestUrl);
        }

        return apiInformation;
    }

    private ApiLifecycleStage? FindMatchingApiLifecycleStage(RequestLog request, string requestUrl, ApiInformation apiInformation)
    {
        // determine version based on:
        // - URL path and query parameters
        // - headers
        ApiInformationVersion? version = null;
        foreach (var apiVersion in apiInformation.Versions)
        {
            // check URL
            if (requestUrl.Contains(apiVersion.Name) || requestUrl.Contains(apiVersion.Title))
            {
                _logger?.LogDebug("Version {version} found in URL {url}", $"{apiVersion.Name}/{apiVersion.Title}", requestUrl);
                version = apiVersion;
                break;
            }

            // check headers
            Debug.Assert(request.Context is not null);
            var header = request.Context.Session.HttpClient.Request.Headers.FirstOrDefault(
                h => h.Value.Contains(apiVersion.Name) ||
                h.Value.Contains(apiVersion.Title)
            );
            if (header is not null)
            {
                _logger?.LogDebug("Version {version} found in header {header}", $"{apiVersion.Name}/{apiVersion.Title}", header.Name);
                version = apiVersion;
                break;
            }
        }

        if (version is null)
        {
            _logger?.LogDebug("No matching version found for {request}", requestUrl);
            return null;
        }

        return version.LifecycleStage;
    }

    private async Task<Collection<ApiVersion>?> LoadApiVersionsFromApiCenter(Api api)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogDebug("Loading versions for {api}...", api.Properties?.Title);

        var res = await _httpClient.GetStringAsync($"https://management.azure.com{api.Id}/versions?api-version=2024-03-01");
        return JsonSerializer.Deserialize<Collection<ApiVersion>>(res, _jsonSerializerOptions);
    }

    async Task<Collection<Api>?> LoadApisFromApiCenter()
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogInformation("Loading APIs from API Center...");

        var res = await _httpClient.GetStringAsync($"https://management.azure.com/subscriptions/{_configuration.SubscriptionId}/resourceGroups/{_configuration.ResourceGroupName}/providers/Microsoft.ApiCenter/services/{_configuration.ServiceName}/workspaces/{_configuration.WorkspaceName}/apis?api-version=2024-03-01");
        return JsonSerializer.Deserialize<Collection<Api>>(res, _jsonSerializerOptions);
    }

    async Task<Collection<ApiDeployment>?> LoadApiDeployments(Api api)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogDebug("Loading API deployments for {api}...", api.Properties?.Title);

        var res = await _httpClient.GetStringAsync($"https://management.azure.com{api.Id}/deployments?api-version=2024-03-01");
        return JsonSerializer.Deserialize<Collection<ApiDeployment>>(res, _jsonSerializerOptions);
    }
}