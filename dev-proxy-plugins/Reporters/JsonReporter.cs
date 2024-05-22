// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Reporters;

public class JsonReporter : BaseReporter
{
    public override string Name => nameof(JsonReporter);
    public override string FileExtension => ".json";

    private readonly Dictionary<Type, Func<object, object>> _transformers = new()
    {
        { typeof(ExecutionSummaryPluginReportByUrl), TransformExecutionSummary },
        { typeof(ExecutionSummaryPluginReportByMessageType), TransformExecutionSummary },
    };

    public JsonReporter(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    protected override string GetReport(KeyValuePair<string, object> report)
    {
        Logger.LogDebug("Serializing report {reportKey}...", report.Key);

        var reportData = report.Value;
        var reportType = reportData.GetType();

        if (_transformers.TryGetValue(reportType, out var transform))
        {
            Logger.LogDebug("Transforming {reportType} using {transform}...", reportType.Name, transform.Method.Name);
            reportData = transform(reportData);
        }
        else
        {
            Logger.LogDebug("No transformer found for {reportType}", reportType.Name);
        }

        if (reportData is string strVal)
        {
            Logger.LogDebug("{reportKey} is a string. Checking if it's JSON...", report.Key);

            try
            {
                JsonSerializer.Deserialize<object>(strVal);
                Logger.LogDebug("{reportKey} is already JSON, ignore", report.Key);
                // already JSON, ignore
                return strVal;
            }
            catch
            {
                Logger.LogDebug("{reportKey} is not JSON, serializing...", report.Key);
            }
        }

        return JsonSerializer.Serialize(reportData, ProxyUtils.JsonSerializerOptions);
    }

    private static object TransformExecutionSummary(object report)
    {
        var executionSummaryReport = (ExecutionSummaryPluginReportBase)report;
        return executionSummaryReport.Data;
    }
}