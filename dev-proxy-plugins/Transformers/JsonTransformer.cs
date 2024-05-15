using System.Text.Json;
using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Transformers;

public class JsonTransformer : BaseProxyPlugin
{
    public override string Name => nameof(JsonTransformer);

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
            _logger?.LogDebug("Transforming report {reportKey}...", report.Key);

            if (report.Value is string strVal)
            {
                _logger?.LogDebug("{reportKey} is a string. Check if it's JSON...", report.Key);

                try
                {
                    JsonSerializer.Deserialize<object>(strVal);
                    _logger?.LogDebug("{reportKey} is already JSON, ignore", report.Key);
                    // already JSON, ignore
                    continue;
                }
                catch
                {
                    _logger?.LogDebug("{reportKey} is not JSON, transforming...", report.Key);
                }
            }

            reports[report.Key] = JsonSerializer.Serialize(report.Value, ProxyUtils.JsonSerializerOptions);
        }

        return Task.CompletedTask;
    }
}