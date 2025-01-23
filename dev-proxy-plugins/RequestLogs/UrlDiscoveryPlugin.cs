// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.RequestLogs;

public class UrlDiscoveryPluginReport
{
    public required List<string> Data { get; init; }
}

public class UrlDiscoveryPlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : BaseReportingPlugin(pluginEvents, context, logger, urlsToWatch, configSection)
{
    public override string Name => nameof(UrlDiscoveryPlugin);
    private readonly ExecutionSummaryPluginConfiguration _configuration = new();

    public override async Task RegisterAsync()
    {
        await base.RegisterAsync();

        ConfigSection?.Bind(_configuration);

        PluginEvents.AfterRecordingStop += AfterRecordingStopAsync;
    }

    private Task AfterRecordingStopAsync(object? sender, RecordingArgs e)
    {
        if (!e.RequestLogs.Any())
        {
            Logger.LogRequest("No messages recorded", MessageType.Skipped);
            return Task.CompletedTask;
        }

        UrlDiscoveryPluginReport report = new()
        {
            Data = [.. e.RequestLogs.Select(log => log.Context?.Session.HttpClient.Request.RequestUri.ToString()).Distinct().Order()]
        };

        StoreReport(report, e);

        return Task.CompletedTask;
    }
}
