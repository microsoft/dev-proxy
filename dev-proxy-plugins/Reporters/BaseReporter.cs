// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Reporters;

public abstract class BaseReporter : BaseProxyPlugin
{
    protected BaseReporter(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public virtual string FileExtension => throw new NotImplementedException();

    public override void Register()
    {
        base.Register();

        PluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    protected abstract string? GetReport(KeyValuePair<string, object> report);

    protected virtual Task AfterRecordingStop(object sender, RecordingArgs e)
    {
        if (!e.GlobalData.ContainsKey(ProxyUtils.ReportsKey) ||
            e.GlobalData[ProxyUtils.ReportsKey] is not Dictionary<string, object> reports ||
            !reports.Any())
        {
            Logger.LogDebug("No reports found");
            return Task.CompletedTask;
        }

        foreach (var report in reports)
        {
            Logger.LogDebug("Transforming report {reportKey}...", report.Key);

            var reportContents = GetReport(report);

            if (string.IsNullOrEmpty(reportContents))
            {
                Logger.LogDebug("Report {reportKey} is empty, ignore", report.Key);
                continue;
            }

            var fileName = $"{report.Key}_{Name}{FileExtension}";
            Logger.LogDebug("File name for report {report}: {fileName}", report.Key, fileName);

            if (File.Exists(fileName))
            {
                Logger.LogDebug("File {fileName} already exists, appending timestamp", fileName);
                fileName = $"{report.Key}_{Name}_{DateTime.Now:yyyyMMddHHmmss}{FileExtension}";
            }

            Logger.LogInformation("Writing report {reportKey} to {fileName}...", report.Key, fileName);
            File.WriteAllText(fileName, reportContents);
        }

        return Task.CompletedTask;
    }
}