// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Reporters;

public abstract class BaseReporter : BaseProxyPlugin
{
    public virtual string FileExtension => throw new NotImplementedException();

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    protected abstract string? GetReport(KeyValuePair<string, object> report);

    protected virtual Task AfterRecordingStop(object sender, RecordingArgs e)
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

            var reportContents = GetReport(report);

            if (string.IsNullOrEmpty(reportContents))
            {
                _logger?.LogDebug("Report {reportKey} is empty, ignore", report.Key);
                continue;
            }

            var fileName = $"{report.Key}_{Name}{FileExtension}";
            _logger?.LogDebug("File name for report {report}: {fileName}", report.Key, fileName);

            if (File.Exists(fileName))
            {
                _logger?.LogDebug("File {fileName} already exists, appending timestamp", fileName);
                fileName = $"{report.Key}_{Name}_{DateTime.Now:yyyyMMddHHmmss}{FileExtension}";
            }

            _logger?.LogDebug("Writing report {reportKey} to {fileName}...", report.Key, fileName);
            File.WriteAllText(fileName, reportContents);
        }

        return Task.CompletedTask;
    }
}