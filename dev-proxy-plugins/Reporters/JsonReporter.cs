// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs;
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

    protected override string GetReport(KeyValuePair<string, object> report)
    {
        _logger?.LogDebug("Serializing report {reportKey}...", report.Key);

        var reportData = report.Value;
        var reportType = reportData.GetType();

        if (_transformers.TryGetValue(reportType, out var transform))
        {
            _logger?.LogDebug("Transforming {reportType} using {transform}...", reportType.Name, transform.Method.Name);
            reportData = transform(reportData);
        }
        else
        {
            _logger?.LogDebug("No transformer found for {reportType}", reportType.Name);
        }

        if (reportData is string strVal)
        {
            _logger?.LogDebug("{reportKey} is a string. Checking if it's JSON...", report.Key);

            try
            {
                JsonSerializer.Deserialize<object>(strVal);
                _logger?.LogDebug("{reportKey} is already JSON, ignore", report.Key);
                // already JSON, ignore
                return strVal;
            }
            catch
            {
                _logger?.LogDebug("{reportKey} is not JSON, serializing...", report.Key);
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