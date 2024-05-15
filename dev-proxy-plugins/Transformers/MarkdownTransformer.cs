using System.Text;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Transformers;

public class MarkdownTransformer : BaseProxyPlugin
{
    public override string Name => nameof(MarkdownTransformer);

    private readonly Dictionary<Type, Func<object, string>> _transformers = new()
    {
        { typeof(ExecutionSummaryPlugin), TransformExecutionSummary },
        { typeof(ExecutionSummaryPluginReportByUrl), TransformExecutionSummaryByUrl },
        { typeof(ExecutionSummaryPluginReportByMessageType), TransformExecutionSummaryByMessageType }
    };

    private const string _requestsInterceptedMessage = "Requests intercepted";
    private const string _requestsPassedThroughMessage = "Requests passed through";

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
            _logger?.LogDebug("Transforming {report}...", report.Key);

            var reportType = report.Value.GetType();

            if (_transformers.TryGetValue(reportType, out var transform))
            {
                _logger?.LogDebug("Transforming {reportType} using {transform}...", reportType.Name, transform.Method.Name);

                reports[report.Key] = transform(report.Value);
            }
            else
            {
                _logger?.LogDebug("No transformer found for {reportType}", reportType.Name);
            }
        }

        return Task.CompletedTask;
    }

    private static string TransformExecutionSummary(object executionSummary)
    {
        var sb = new StringBuilder();

        // sb.AppendLine($"# Execution Summary");
        // sb.AppendLine($"## Total Requests: {executionSummary.TotalRequests}");
        // sb.AppendLine($"## Total Duration: {executionSummary.TotalDuration}");
        // sb.AppendLine($"## Average Duration: {executionSummary.AverageDuration}");
        // sb.AppendLine($"## Total Errors: {executionSummary.TotalErrors}");

        return sb.ToString();
    }

    private static string TransformExecutionSummaryByMessageType(object report)
    {
        var executionSummaryReport = (ExecutionSummaryPluginReportByMessageType)report;

        var sb = new StringBuilder();

        sb.AppendLine("# Dev Proxy execution summary");
        sb.AppendLine();
        sb.AppendLine($"Date: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
        sb.AppendLine();

        sb.AppendLine("## Message types");

        var data = executionSummaryReport.Data;
        var sortedMessageTypes = data.Keys.OrderBy(k => k);
        foreach (var messageType in sortedMessageTypes)
        {
            sb.AppendLine();
            sb.AppendLine($"### {messageType}");

            if (messageType == _requestsInterceptedMessage ||
                messageType == _requestsPassedThroughMessage)
            {
                sb.AppendLine();

                var sortedMethodAndUrls = data[messageType][messageType].Keys.OrderBy(k => k);
                foreach (var methodAndUrl in sortedMethodAndUrls)
                {
                    sb.AppendLine($"- ({data[messageType][messageType][methodAndUrl]}) {methodAndUrl}");
                }
            }
            else
            {
                var sortedMessages = data[messageType].Keys.OrderBy(k => k);
                foreach (var message in sortedMessages)
                {
                    sb.AppendLine();
                    sb.AppendLine($"#### {message}");
                    sb.AppendLine();

                    var sortedMethodAndUrls = data[messageType][message].Keys.OrderBy(k => k);
                    foreach (var methodAndUrl in sortedMethodAndUrls)
                    {
                        sb.AppendLine($"- ({data[messageType][message][methodAndUrl]}) {methodAndUrl}");
                    }
                }
            }
        }

        AddExecutionSummaryReportSummary(executionSummaryReport.Logs, sb);

        return sb.ToString();
    }

    private static string TransformExecutionSummaryByUrl(object report)
    {
        var executionSummaryReport = (ExecutionSummaryPluginReportByUrl)report;

        var sb = new StringBuilder();

        sb.AppendLine("# Dev Proxy execution summary");
        sb.AppendLine();
        sb.AppendLine($"Date: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
        sb.AppendLine();

        sb.AppendLine("## Requests");

        var data = executionSummaryReport.Data;
        var sortedMethodAndUrls = data.Keys.OrderBy(k => k);
        foreach (var methodAndUrl in sortedMethodAndUrls)
        {
            sb.AppendLine();
            sb.AppendLine($"### {methodAndUrl}");

            var sortedMessageTypes = data[methodAndUrl].Keys.OrderBy(k => k);
            foreach (var messageType in sortedMessageTypes)
            {
                sb.AppendLine();
                sb.AppendLine($"#### {messageType}");
                sb.AppendLine();

                var sortedMessages = data[methodAndUrl][messageType].Keys.OrderBy(k => k);
                foreach (var message in sortedMessages)
                {
                    sb.AppendLine($"- ({data[methodAndUrl][messageType][message]}) {message}");
                }
            }
        }

        AddExecutionSummaryReportSummary(executionSummaryReport.Logs, sb);

        return sb.ToString();
    }

    private static void AddExecutionSummaryReportSummary(IEnumerable<RequestLog> requestLogs, StringBuilder sb)
    {
        var data = requestLogs
          .Where(log => log.MessageType != MessageType.InterceptedResponse)
          .Select(log => GetReadableMessageTypeForSummary(log.MessageType))
          .OrderBy(log => log)
          .GroupBy(log => log)
          .ToDictionary(group => group.Key, group => group.Count());

        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("Category|Count");
        sb.AppendLine("--------|----:");

        foreach (var messageType in data.Keys)
        {
            sb.AppendLine($"{messageType}|{data[messageType]}");
        }
    }

    private static string GetReadableMessageTypeForSummary(MessageType messageType) => messageType switch
    {
        MessageType.Chaos => "Requests with chaos",
        MessageType.Failed => "Failures",
        MessageType.InterceptedRequest => _requestsInterceptedMessage,
        MessageType.Mocked => "Requests mocked",
        MessageType.PassedThrough => _requestsPassedThroughMessage,
        MessageType.Tip => "Tips",
        MessageType.Warning => "Warnings",
        _ => "Unknown"
    };
}