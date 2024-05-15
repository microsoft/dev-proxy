using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Reporters;

internal class FileReporterConfiguration
{
    public bool SeparateFilePerReport { get; set; } = true;
}

public class FileReporter : BaseProxyPlugin
{
    public override string Name => nameof(FileReporter);
    private FileReporterConfiguration _configuration = new();

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        configSection?.Bind(_configuration);

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

        if (_configuration.SeparateFilePerReport)
        {
            foreach (var report in reports)
            {
                var fileName = report.Key.Contains('.') ? report.Key : $"{report.Key}_{DateTime.Now:yyyyMMddHHmmss}.txt";
                var reportContent = report.Value.ToString();
                File.WriteAllText(fileName, reportContent);
            }
        }
        else
        {
            var fileName = $"report_{DateTime.Now:yyyyMMddHHmmss}.txt";
            var allReports = string.Join(Environment.NewLine, reports.Select(r => $"{r.Key}:{Environment.NewLine}{r.Value}"));
            File.WriteAllText(fileName, allReports);
        }

        return Task.CompletedTask;
    }
}