// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs.ApiCenter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.RequestLogs;

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
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ApiCenterProductionVersionPluginReportItemStatus Status { get; init; }
    public string? Recommendation { get; init; }
}

public class ApiCenterProductionVersionPluginReport : List<ApiCenterProductionVersionPluginReportItem>;

internal class ApiCenterProductionVersionPluginConfiguration
{
    public string SubscriptionId { get; set; } = "";
    public string ResourceGroupName { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string WorkspaceName { get; set; } = "default";
}

public class ApiCenterProductionVersionPlugin : BaseReportingPlugin
{
    private ApiCenterProductionVersionPluginConfiguration _configuration = new();
    private ApiCenterClient? _apiCenterClient;
    private Api[]? _apis;

    public ApiCenterProductionVersionPlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override string Name => nameof(ApiCenterProductionVersionPlugin);

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
            .Where(
                l => l.MessageType == MessageType.InterceptedRequest &&
                l.Context?.Session is not null
            );
        if (!interceptedRequests.Any())
        {
            Logger.LogDebug("No requests to process");
            return;
        }

        Logger.LogInformation("Checking if recorded API requests use production APIs as defined in API Center...");

        Debug.Assert(_apiCenterClient is not null);

        if (_apis is null)
        {
            _apis = await _apiCenterClient.GetApis();
        }

        if (_apis == null || !_apis.Any())
        {
            Logger.LogInformation("No APIs found in API Center");
            return;
        }

        foreach (var api in _apis)
        {
            Debug.Assert(api.Id is not null);

            await api.LoadVersions(_apiCenterClient);
            if (api.Versions?.Any() != true)
            {
                Logger.LogInformation("No versions found for {api}", api.Properties?.Title);
                continue;
            }

            foreach (var versionFromApiCenter in api.Versions)
            {
                Debug.Assert(versionFromApiCenter.Id is not null);

                await versionFromApiCenter.LoadDefinitions(_apiCenterClient);
                if (versionFromApiCenter.Definitions?.Any() != true)
                {
                    Logger.LogDebug("No definitions found for version {versionId}", versionFromApiCenter.Id);
                    continue;
                }

                var definitions = new List<ApiDefinition>();
                foreach (var definitionFromApiCenter in versionFromApiCenter.Definitions)
                {
                    Debug.Assert(definitionFromApiCenter.Id is not null);

                    await definitionFromApiCenter.LoadOpenApiDefinition(_apiCenterClient, Logger);

                    if (definitionFromApiCenter.Definition is null)
                    {
                        Logger.LogDebug("API definition not found for {definitionId}", definitionFromApiCenter.Id);
                        continue;
                    }

                    if (!definitionFromApiCenter.Definition.Servers.Any())
                    {
                        Logger.LogDebug("No servers found for API definition {definitionId}", definitionFromApiCenter.Id);
                        continue;
                    }

                    definitions.Add(definitionFromApiCenter);
                }

                versionFromApiCenter.Definitions = definitions.ToArray();
            }
        }

        Logger.LogInformation("Analyzing recorded requests...");

        var report = new ApiCenterProductionVersionPluginReport();

        foreach (var request in interceptedRequests)
        {
            var methodAndUrlString = request.MessageLines.First();
            var methodAndUrl = methodAndUrlString.Split(' ');
            var (method, url) = (methodAndUrl[0], methodAndUrl[1]);
            if (method == "OPTIONS")
            {
                continue;
            }

            var api = _apis.FindApiByUrl(url, Logger);
            if (api == null)
            {
                report.Add(new()
                {
                    Method = method,
                    Url = url,
                    Status = ApiCenterProductionVersionPluginReportItemStatus.NotRegistered
                });
                continue;
            }

            var version = api.GetVersion(request, url, Logger);
            if (version is null)
            {
                report.Add(new()
                {
                    Method = method,
                    Url = url,
                    Status = ApiCenterProductionVersionPluginReportItemStatus.NotRegistered
                });
                continue;
            }

            Debug.Assert(version.Properties is not null);
            var lifecycleStage = version.Properties.LifecycleStage;

            if (lifecycleStage != ApiLifecycleStage.Production)
            {
                Debug.Assert(api.Versions is not null);

                var productionVersions = api.Versions
                    .Where(v => v.Properties?.LifecycleStage == ApiLifecycleStage.Production)
                    .Select(v => v.Properties?.Title);

                var recommendation = productionVersions.Any() ?
                    string.Format("Request {0} uses API version {1} which is defined as {2}. Upgrade to a production version of the API. Recommended versions: {3}", methodAndUrlString, api.Versions.First(v => v.Properties?.LifecycleStage == lifecycleStage).Properties?.Title, lifecycleStage, string.Join(", ", productionVersions)) :
                    string.Format("Request {0} uses API version {1} which is defined as {2}.", methodAndUrlString, api.Versions.First(v => v.Properties?.LifecycleStage == lifecycleStage).Properties?.Title, lifecycleStage);

                Logger.LogWarning(recommendation);
                report.Add(new()
                {
                    Method = method,
                    Url = url,
                    Status = ApiCenterProductionVersionPluginReportItemStatus.NonProduction,
                    Recommendation = recommendation
                });
            }
            else
            {
                report.Add(new()
                {
                    Method = method,
                    Url = url,
                    Status = ApiCenterProductionVersionPluginReportItemStatus.Production
                });
            }
        }

        StoreReport(report, e);
    }
}