// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Diagnostics;
using Azure.Identity;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins;
using Microsoft.DevProxy.Plugins.RequestLogs.ApiCenter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

internal class ApiInformation
{
    public ApiInformationVersion[] Versions { get; set; } = [];
    // deployment.properties.server.runtimeUri[]
    public string[] Urls { get; set; } = [];
}

internal class ApiInformationVersion
{
    public ApiInformationVersionInformation Version { get; set; } = new();
    public ApiLifecycleStage? LifecycleStage { get; set; }
}

internal class ApiInformationVersionInformation
{
    // properties.title
    public string Name { get; set; } = "";
    // name
    public string Id { get; set; } = "";
}

internal class ApiCenterProductionVersionPluginConfiguration
{
    public string SubscriptionId { get; set; } = "";
    public string ResourceGroupName { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string WorkspaceName { get; set; } = "default";
    public bool ExcludeDevCredentials { get; set; } = false;
    public bool ExcludeProdCredentials { get; set; } = true;
}

public class ApiCenterProductionVersionPlugin : BaseProxyPlugin
{
    private ApiCenterProductionVersionPluginConfiguration _configuration = new();
    private readonly string[] _scopes = ["https://management.azure.com/.default"];
    private TokenCredential _credential = new DefaultAzureCredential();
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
        if (_configuration.ExcludeDevCredentials && _configuration.ExcludeProdCredentials)
        {
            _logger?.LogError("Both ExcludeDevCredentials and ExcludeProdCredentials are set to true. You need to use at least one set of credentials The {plugin} will not be used.", Name);
            return;
        }

        var credentials = new List<TokenCredential>();
        if (!_configuration.ExcludeDevCredentials)
        {
            credentials.AddRange([
                new SharedTokenCacheCredential(),
                new VisualStudioCredential(),
                new VisualStudioCodeCredential(),
                new AzureCliCredential(),
                new AzurePowerShellCredential(),
                new AzureDeveloperCliCredential(),
            ]);
        }
        if (!_configuration.ExcludeProdCredentials)
        {
            credentials.AddRange([
                new EnvironmentCredential(),
                new WorkloadIdentityCredential(),
                new ManagedIdentityCredential()
            ]);
        }
        _credential = new ChainedTokenCredential(credentials.ToArray());

        if (_logger?.LogLevel == LogLevel.Debug)
        {
            var consoleListener = AzureEventSourceListener.CreateConsoleLogger(EventLevel.Verbose);
        }

        _logger?.LogDebug("[{now}] Plugin {plugin} checking Azure auth...", DateTime.Now, Name);
        try
        {
            _ = _credential.GetTokenAsync(new TokenRequestContext(_scopes), CancellationToken.None).Result;
        }
        catch (AuthenticationFailedException ex)
        {
            _logger?.LogError(ex, "Failed to authenticate with Azure. The {plugin} will not be used.", Name);
            return;
        }
        _logger?.LogDebug("[{now}] Plugin {plugin} auth confirmed...", DateTime.Now, Name);

        var authenticationHandler = new AuthenticationDelegatingHandler(_credential, _scopes)
        {
            InnerHandler = new HttpClientHandler()
        };
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

        var apis = await LoadApisFromApiCenter();
        if (apis == null || !apis.Value.Any())
        {
            _logger?.LogInformation("No APIs found in API Center");
            return;
        }

        var apisInformation = new List<ApiInformation>();
        foreach (var api in apis.Value)
        {
            var apiVersions = await LoadApiVersions(api);
            if (apiVersions == null || !apiVersions.Value.Any())
            {
                _logger?.LogInformation("No versions found for {api}", api.Properties?.Title);
                continue;
            }

            var apiInformationVersion = apiVersions.Value.Select(v => new ApiInformationVersion
            {
                Version = new ApiInformationVersionInformation
                {
                    Name = v.Properties?.Title ?? "",
                    Id = v.Id ?? ""
                },
                LifecycleStage = v.Properties?.LifecycleStage
            }).ToArray();

            var apiDeployments = await LoadApiDeployments(api);
            if (apiDeployments == null || !apiDeployments.Value.Any())
            {
                _logger?.LogInformation("No deployments found for {api}", api.Properties?.Title);
                continue;
            }

            apisInformation.Add(new ApiInformation
            {
                Versions = apiInformationVersion,
                Urls = apiDeployments.Value.SelectMany(d => d.Properties?.Server?.RuntimeUri ?? Array.Empty<string>()).ToArray()
            });
        }

        _logger?.LogInformation("Analyzing recorded requests...");

        foreach (var request in interceptedRequests)
        {
            var urlAndMethodString = request.MessageLines.First();
            var urlAndMethod = urlAndMethodString.Split(' ');

            var apiInformation = FindMatchingApiInformation(urlAndMethod[1], apisInformation);
            if (apiInformation == null)
            {
                continue;
            }

            var lifecycleStage = FindMatchingApiLifecycleStage(request, urlAndMethod[1], apiInformation);
            if (lifecycleStage == null)
            {
                continue;
            }

            if (lifecycleStage != ApiLifecycleStage.Production)
            {
                var productionVersions = apiInformation.Versions
                    .Where(v => v.LifecycleStage == ApiLifecycleStage.Production)
                    .Select(v => v.Version.Name);

                if (productionVersions.Any())
                {
                    _logger?.LogWarning("Request {request} uses API version {version} which is defined as {lifecycleStage}. Upgrade to a production version of the API. Recommended versions: {versions}", urlAndMethodString, apiInformation.Versions.First(v => v.LifecycleStage == lifecycleStage).Version.Name, lifecycleStage, string.Join(", ", productionVersions));
                }
                else
                {
                    _logger?.LogWarning("Request {request} uses API version {version} which is defined as {lifecycleStage}.", urlAndMethodString, apiInformation.Versions.First(v => v.LifecycleStage == lifecycleStage).Version.Name, lifecycleStage);                    
                }
            }
        }

        _logger?.LogInformation("DONE");
    }

    private ApiInformation? FindMatchingApiInformation(string requestUrl, List<ApiInformation>? apisInformation)
    {
        var apiInformation = apisInformation?.FirstOrDefault(a => a.Urls.Any(u => requestUrl.StartsWith(u)));
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
            if (requestUrl.Contains(apiVersion.Version.Id) || requestUrl.Contains(apiVersion.Version.Name))
            {
                _logger?.LogDebug("Version {version} found in URL {url}", $"{apiVersion.Version.Id}/{apiVersion.Version.Name}", requestUrl);
                version = apiVersion;
                break;
            }

            // check headers
            Debug.Assert(request.Context is not null);
            var header = request.Context.Session.HttpClient.Request.Headers.FirstOrDefault(
                h => h.Value.Contains(apiVersion.Version.Id) ||
                h.Value.Contains(apiVersion.Version.Name)
            );
            if (header is not null)
            {
                _logger?.LogDebug("Version {version} found in header {header}", $"{apiVersion.Version.Id}/{apiVersion.Version.Name}", header.Name);
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

    private async Task<Collection<ApiVersion>?> LoadApiVersions(Api api)
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