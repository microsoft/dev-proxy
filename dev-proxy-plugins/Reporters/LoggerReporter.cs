using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Reporters;

public class LoggerReporter : BaseProxyPlugin
{
    public override string Name => nameof(LoggerReporter);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    private Task AfterRecordingStop(object sender, RecordingArgs e)
    {
        if (!e.GlobalData.ContainsKey(ProxyUtils.ReportsKey) ||
            e.GlobalData[ProxyUtils.ReportsKey] is not Dictionary<string, object> reports ||
            !reports.Any())
        {
            _logger?.LogDebug("No reports found");
            return Task.CompletedTask;
        }

        foreach (var report in reports)
        {
            _logger?.LogInformation(report.Value.ToString());
        }

        return Task.CompletedTask;
    }
}