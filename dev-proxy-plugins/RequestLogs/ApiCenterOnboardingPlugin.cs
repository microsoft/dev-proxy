// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Core.Diagnostics;
using Azure.Identity;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs.ApiCenter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace Microsoft.DevProxy.Plugins.RequestLogs;

public class ApiCenterOnboardingPluginReportExistingApiInfo
{
    public required string MethodAndUrl { get; init; }
    public required string ApiDefinitionId { get; init; }
    public required string OperationId { get; init; }
}

public class ApiCenterOnboardingPluginReportNewApiInfo
{
    public required string Method { get; init; }
    public required string Url { get; init; }
}

public class ApiCenterOnboardingPluginReport
{
    public required ApiCenterOnboardingPluginReportExistingApiInfo[] ExistingApis { get; init; }
    public required ApiCenterOnboardingPluginReportNewApiInfo[] NewApis { get; init; }
}

internal class ApiCenterOnboardingPluginConfiguration
{
    public string SubscriptionId { get; set; } = "";
    public string ResourceGroupName { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string WorkspaceName { get; set; } = "default";
    public bool CreateApicEntryForNewApis { get; set; } = true;
}

public class ApiCenterOnboardingPlugin : BaseReportingPlugin
{
    private ApiCenterOnboardingPluginConfiguration _configuration = new();
    private readonly string[] _scopes = ["https://management.azure.com/.default"];
    private TokenCredential _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions()
    {
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

    public override string Name => nameof(ApiCenterOnboardingPlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        configSection?.Bind(_configuration);

        if (string.IsNullOrEmpty(_configuration.SubscriptionId))
        {
            _logger?.LogError("Specify SubscriptionId in the {plugin} configuration. The {plugin} will not be used.", Name, Name);
            return;
        }
        if (string.IsNullOrEmpty(_configuration.ResourceGroupName))
        {
            _logger?.LogError("Specify ResourceGroupName in the {plugin} configuration. The {plugin} will not be used.", Name, Name);
            return;
        }
        if (string.IsNullOrEmpty(_configuration.ServiceName))
        {
            _logger?.LogError("Specify ServiceName in the {plugin} configuration. The {plugin} will not be used.", Name, Name);
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
        if (!e.RequestLogs.Any())
        {
            _logger?.LogDebug("No requests to process");
            return;
        }

        _logger?.LogInformation("Checking if recorded API requests belong to APIs in API Center...");

        Debug.Assert(_httpClient is not null);

        var apis = await LoadApisFromApiCenter();
        if (apis == null || !apis.Value.Any())
        {
            _logger?.LogInformation("No APIs found in API Center");
            return;
        }

        var apiDefinitions = await LoadApiDefinitions(apis.Value);

        var newApis = new List<(string method, string url)>();
        var interceptedRequests = e.RequestLogs
            .Where(l => l.MessageType == MessageType.InterceptedRequest)
            .Select(request =>
            {
                var methodAndUrl = request.MessageLines.First().Split(' ');
                return (method: methodAndUrl[0], url: methodAndUrl[1]);
            })
            .Where(r => !r.method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            .Distinct();

        var existingApis = new List<ApiCenterOnboardingPluginReportExistingApiInfo>();

        foreach (var request in interceptedRequests)
        {
            var (method, url) = request;

            _logger?.LogDebug("Processing request {method} {url}...", method, url);

            var apiDefinition = apiDefinitions.FirstOrDefault(x => url.StartsWith(x.Key, StringComparison.OrdinalIgnoreCase)).Value;
            if (apiDefinition is null ||
                apiDefinition.Id is null)
            {
                _logger?.LogDebug("No matching API definition not found for {url}. Adding new API...", url);
                newApis.Add((method, url));
                continue;
            }

            await EnsureApiDefinition(apiDefinition);

            if (apiDefinition.Definition is null)
            {
                _logger?.LogDebug("API definition not found for {url} so nothing to compare to. Adding new API...", url);
                newApis.Add(new(method, url));
                continue;
            }

            var pathItem = FindMatchingPathItem(url, apiDefinition.Definition);
            if (pathItem is null)
            {
                _logger?.LogDebug("No matching path found for {url}. Adding new API...", url);
                newApis.Add(new(method, url));
                continue;
            }

            var operation = pathItem.Operations.FirstOrDefault(x => x.Key.ToString().Equals(method, StringComparison.OrdinalIgnoreCase)).Value;
            if (operation is null)
            {
                _logger?.LogDebug("No matching operation found for {method} {url}. Adding new API...", method, url);

                newApis.Add(new(method, url));
                continue;
            }

            existingApis.Add(new ApiCenterOnboardingPluginReportExistingApiInfo
            {
                MethodAndUrl = $"{method} {url}",
                ApiDefinitionId = apiDefinition.Id,
                OperationId = operation.OperationId
            });
        }

        if (!newApis.Any())
        {
            _logger?.LogInformation("No new APIs found");
            StoreReport(new ApiCenterOnboardingPluginReport
            {
                ExistingApis = existingApis.ToArray(),
                NewApis = Array.Empty<ApiCenterOnboardingPluginReportNewApiInfo>()
            }, e);
            return;
        }

        // dedupe newApis
        newApis = newApis.Distinct().ToList();

        StoreReport(new ApiCenterOnboardingPluginReport
        {
            ExistingApis = existingApis.ToArray(),
            NewApis = newApis.Select(a => new ApiCenterOnboardingPluginReportNewApiInfo
            {
                Method = a.method,
                Url = a.url
            }).ToArray()
        }, e);

        var apisPerSchemeAndHost = newApis.GroupBy(x =>
        {
            var u = new Uri(x.url);
            return u.GetLeftPart(UriPartial.Authority);
        });

        var newApisMessageChunks = new List<string>(["New APIs that aren't registered in Azure API Center:", ""]);
        foreach (var apiPerHost in apisPerSchemeAndHost)
        {
            newApisMessageChunks.Add($"{apiPerHost.Key}:");
            newApisMessageChunks.AddRange(apiPerHost.Select(a => $"  {a.method} {a.url}"));
        }

        _logger?.LogInformation(string.Join(Environment.NewLine, newApisMessageChunks));

        if (!_configuration.CreateApicEntryForNewApis)
        {
            return;
        }

        var generatedOpenApiSpecs = e.GlobalData.TryGetValue(OpenApiSpecGeneratorPlugin.GeneratedOpenApiSpecsKey, out var specs) ? specs as Dictionary<string, string> : new();
        await CreateApisInApiCenter(apisPerSchemeAndHost, generatedOpenApiSpecs!);
    }

    async Task CreateApisInApiCenter(IEnumerable<IGrouping<string, (string method, string url)>> apisPerHost, Dictionary<string, string> generatedOpenApiSpecs)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogInformation("{newLine}Creating new API entries in API Center...", Environment.NewLine);

        foreach (var apiPerHost in apisPerHost)
        {
            var schemeAndHost = apiPerHost.Key;

            var api = await CreateApi(schemeAndHost, apiPerHost);
            if (api is null)
            {
                continue;
            }

            Debug.Assert(api.Id is not null);

            if (!generatedOpenApiSpecs.TryGetValue(schemeAndHost, out var openApiSpecFilePath))
            {
                _logger?.LogDebug("No OpenAPI spec found for {host}", schemeAndHost);
                continue;
            }

            var apiVersion = await CreateApiVersion(api.Id);
            if (apiVersion is null)
            {
                continue;
            }

            Debug.Assert(apiVersion.Id is not null);

            var apiDefinition = await CreateApiDefinition(apiVersion.Id);
            if (apiDefinition is null)
            {
                continue;
            }

            Debug.Assert(apiDefinition.Id is not null);

            await ImportApiDefinition(apiDefinition.Id, openApiSpecFilePath);
        }
    }

    async Task<Api?> CreateApi(string schemeAndHost, IEnumerable<(string method, string url)> apiRequests)
    {
        Debug.Assert(_httpClient is not null);

        // trim to 50 chars which is max length for API name
        var apiName = MaxLength($"new-{schemeAndHost.Replace(".", "-").Replace("http://", "").Replace("https://", "")}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", 50);
        _logger?.LogInformation("  Creating API {apiName} for {host}...", apiName, schemeAndHost);

        var title = $"New APIs: {schemeAndHost}";
        var description = new List<string>(["New APIs discovered by Dev Proxy", ""]);
        description.AddRange(apiRequests.Select(a => $"  {a.method} {a.url}").ToArray());
        var payload = new
        {
            properties = new
            {
                title,
                description = string.Join(Environment.NewLine, description),
                kind = "REST",
                type = "rest"
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload, _jsonSerializerOptions), Encoding.UTF8, "application/json");
        var res = await _httpClient.PutAsync($"https://management.azure.com/subscriptions/{_configuration.SubscriptionId}/resourceGroups/{_configuration.ResourceGroupName}/providers/Microsoft.ApiCenter/services/{_configuration.ServiceName}/workspaces/{_configuration.WorkspaceName}/apis/{apiName}?api-version=2024-03-01", content);
        if (res.IsSuccessStatusCode)
        {
            _logger?.LogDebug("API created successfully");
        }
        else
        {
            _logger?.LogError("Failed to create API {apiName} for {host}", apiName, schemeAndHost);
        }
        var resContent = await res.Content.ReadAsStringAsync();
        _logger?.LogDebug(resContent);

        if (res.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<Api>(resContent, _jsonSerializerOptions);
        }
        else
        {
            return null;
        }
    }

    async Task<ApiVersion?> CreateApiVersion(string apiId)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogDebug("  Creating API version for {api}...", apiId);

        var payload = new
        {
            properties = new
            {
                title = "v1.0",
                lifecycleStage = "production"
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload, _jsonSerializerOptions), Encoding.UTF8, "application/json");
        var res = await _httpClient.PutAsync($"https://management.azure.com{apiId}/versions/v1-0?api-version=2024-03-01", content);
        if (res.IsSuccessStatusCode)
        {
            _logger?.LogDebug("API version created successfully");
        }
        else
        {
            _logger?.LogError("Failed to create API version for {api}", apiId.Substring(apiId.LastIndexOf('/')));
        }
        var resContent = await res.Content.ReadAsStringAsync();
        _logger?.LogDebug(resContent);

        if (res.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<ApiVersion>(resContent, _jsonSerializerOptions);
        }
        else
        {
            return null;
        }
    }

    async Task<ApiDefinition?> CreateApiDefinition(string apiVersionId)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogDebug("  Creating API definition for {api}...", apiVersionId);

        var payload = new
        {
            properties = new
            {
                title = "OpenAPI"
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload, _jsonSerializerOptions), Encoding.UTF8, "application/json");
        var res = await _httpClient.PutAsync($"https://management.azure.com{apiVersionId}/definitions/openapi?api-version=2024-03-01", content);
        if (res.IsSuccessStatusCode)
        {
            _logger?.LogDebug("API definition created successfully");
        }
        else
        {
            _logger?.LogError("Failed to create API definition for {apiVersion}", apiVersionId);
        }
        var resContent = await res.Content.ReadAsStringAsync();
        _logger?.LogDebug(resContent);

        if (res.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<ApiDefinition>(resContent, _jsonSerializerOptions);
        }
        else
        {
            return null;
        }
    }

    async Task ImportApiDefinition(string apiDefinitionId, string openApiSpecFilePath)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogDebug("  Importing API definition for {api}...", apiDefinitionId);

        var openApiSpec = File.ReadAllText(openApiSpecFilePath);
        var payload = new
        {
            format = "inline",
            value = openApiSpec,
            specification = new
            {
                name = "openapi",
                version = "3.0.1"
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload, _jsonSerializerOptions), Encoding.UTF8, "application/json");
        var res = await _httpClient.PostAsync($"https://management.azure.com{apiDefinitionId}/importSpecification?api-version=2024-03-01", content);
        if (res.IsSuccessStatusCode)
        {
            _logger?.LogDebug("API definition imported successfully");
        }
        else
        {
            _logger?.LogError("Failed to import API definition for {apiDefinition}. Status: {status}, reason: {reason}", apiDefinitionId, res.StatusCode, res.ReasonPhrase);
        }
    }

    async Task<Collection<Api>?> LoadApisFromApiCenter()
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogInformation("Loading APIs from API Center...");

        var res = await _httpClient.GetStringAsync($"https://management.azure.com/subscriptions/{_configuration.SubscriptionId}/resourceGroups/{_configuration.ResourceGroupName}/providers/Microsoft.ApiCenter/services/{_configuration.ServiceName}/workspaces/{_configuration.WorkspaceName}/apis?api-version=2024-03-01");
        return JsonSerializer.Deserialize<Collection<Api>>(res, _jsonSerializerOptions);
    }

    OpenApiPathItem? FindMatchingPathItem(string requestUrl, OpenApiDocument openApiDocument)
    {
        foreach (var server in openApiDocument.Servers)
        {
            _logger?.LogDebug("Checking server URL {serverUrl}...", server.Url);

            if (!requestUrl.StartsWith(server.Url, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Request URL {requestUrl} does not match server URL {serverUrl}", requestUrl, server.Url);
                continue;
            }

            var serverUrl = new Uri(server.Url);
            var serverPath = serverUrl.AbsolutePath.TrimEnd('/');
            var requestUri = new Uri(requestUrl);
            var urlPathFromRequest = requestUri.GetLeftPart(UriPartial.Path).Replace(server.Url.TrimEnd('/'), "", StringComparison.OrdinalIgnoreCase);

            foreach (var path in openApiDocument.Paths)
            {
                var urlPathFromSpec = path.Key;
                _logger?.LogDebug("Checking path {urlPath}...", urlPathFromSpec);

                // check if path contains parameters. If it does,
                // replace them with regex
                if (urlPathFromSpec.Contains('{'))
                {
                    _logger?.LogDebug("Path {urlPath} contains parameters and will be converted to Regex", urlPathFromSpec);

                    // force replace all parameters with regex
                    // this is more robust than replacing parameters by name
                    // because it's possible to define parameters both on the path
                    // and operations and sometimes, parameters are defined only
                    // on the operation. This way, we cover all cases, and we don't
                    // care about the parameter anyway here
                    urlPathFromSpec = Regex.Replace(urlPathFromSpec, @"\{[^}]+\}", $"([^/]+)");

                    _logger?.LogDebug("Converted path to Regex: {urlPath}", urlPathFromSpec);
                    var regex = new Regex($"^{urlPathFromSpec}$");
                    if (regex.IsMatch(urlPathFromRequest))
                    {
                        _logger?.LogDebug("Regex matches {requestUrl}", urlPathFromRequest);

                        return path.Value;
                    }

                    _logger?.LogDebug("Regex does not match {requestUrl}", urlPathFromRequest);
                }
                else
                {
                    if (urlPathFromRequest.Equals(urlPathFromSpec, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger?.LogDebug("{requestUrl} matches {urlPath}", requestUrl, urlPathFromSpec);

                        return path.Value;
                    }

                    _logger?.LogDebug("{requestUrl} doesn't match {urlPath}", requestUrl, urlPathFromSpec);
                }
            }
        }

        return null;
    }

    async Task<Dictionary<string, ApiDefinition>> LoadApiDefinitions(Api[] apis)
    {
        _logger?.LogInformation("Loading API definitions from API Center...");

        // key is the runtime URI, value is the API definition
        var apiDefinitions = new Dictionary<string, ApiDefinition>();

        foreach (var api in apis)
        {
            Debug.Assert(api.Id is not null);

            _logger?.LogDebug("Loading API definitions for {apiName}...", api.Id);

            // load definitions from deployments
            var deployments = await LoadApiDeployments(api.Id);
            if (deployments != null && deployments.Value.Any())
            {
                foreach (var deployment in deployments.Value)
                {
                    Debug.Assert(deployment.Properties?.Server is not null);
                    Debug.Assert(deployment.Properties?.DefinitionId is not null);

                    if (!deployment.Properties.Server.RuntimeUri.Any())
                    {
                        _logger?.LogDebug("No runtime URIs found for deployment {deploymentName}", deployment.Name);
                        continue;
                    }

                    foreach (var runtimeUri in deployment.Properties.Server.RuntimeUri)
                    {
                        apiDefinitions[runtimeUri] = new ApiDefinition
                        {
                            Id = deployment.Properties.DefinitionId
                        };
                    }
                }
            }
            else
            {
                _logger?.LogDebug("No deployments found for API {api}", api.Id);
            }

            // load definitions from versions
            var versions = await LoadApiVersions(api.Id);
            if (versions != null && versions.Value.Any())
            {
                foreach (var version in versions.Value)
                {
                    Debug.Assert(version.Id is not null);

                    var definitions = await LoadApiDefinitionsForVersion(version.Id);
                    if (definitions != null && definitions.Value.Any())
                    {
                        foreach (var definition in definitions.Value)
                        {
                            Debug.Assert(definition.Id is not null);

                            await EnsureApiDefinition(definition);

                            if (definition.Definition is null)
                            {
                                _logger?.LogDebug("API definition not found for {definitionId}", definition.Id);
                                continue;
                            }

                            if (!definition.Definition.Servers.Any())
                            {
                                _logger?.LogDebug("No servers found for API definition {definitionId}", definition.Id);
                                continue;
                            }

                            foreach (var server in definition.Definition.Servers)
                            {
                                apiDefinitions[server.Url] = definition;
                            }
                        }
                    }
                    else
                    {
                        _logger?.LogDebug("No definitions found for version {versionId}", version.Id);
                    }
                }
            }
            else
            {
                _logger?.LogDebug("No versions found for API {api}", api.Id);
            }
        }

        return apiDefinitions;
    }

    private async Task<Collection<ApiDefinition>?> LoadApiDefinitionsForVersion(string versionId)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogDebug("Loading API definitions for version {id}...", versionId);

        var res = await _httpClient.GetStringAsync($"https://management.azure.com{versionId}/definitions?api-version=2024-03-01");
        return JsonSerializer.Deserialize<Collection<ApiDefinition>>(res, _jsonSerializerOptions);
    }

    private async Task<Collection<ApiVersion>?> LoadApiVersions(string apiId)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogDebug("Loading API versions for {apiName}...", apiId);

        var res = await _httpClient.GetStringAsync($"https://management.azure.com{apiId}/versions?api-version=2024-03-01");
        return JsonSerializer.Deserialize<Collection<ApiVersion>>(res, _jsonSerializerOptions);
    }

    async Task<Collection<ApiDeployment>?> LoadApiDeployments(string apiId)
    {
        Debug.Assert(_httpClient is not null);

        _logger?.LogDebug("Loading API deployments for {apiName}...", apiId);

        var res = await _httpClient.GetStringAsync($"https://management.azure.com{apiId}/deployments?api-version=2024-03-01");
        return JsonSerializer.Deserialize<Collection<ApiDeployment>>(res, _jsonSerializerOptions);
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

    private string MaxLength(string input, int maxLength)
    {
        return input.Length <= maxLength ? input : input.Substring(0, maxLength);
    }
}
