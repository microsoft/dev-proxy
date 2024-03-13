// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.RequestLogs;

internal enum SummaryGroupBy
{
    [JsonPropertyName("url")]
    Url,
    [JsonPropertyName("messageType")]
    MessageType
}

internal class ExecutionSummaryPluginConfiguration
{
    public string FilePath { get; set; } = "";
    public SummaryGroupBy GroupBy { get; set; } = SummaryGroupBy.Url;
}

public class ExecutionSummaryPlugin : BaseProxyPlugin
{
    public override string Name => nameof(ExecutionSummaryPlugin);
    private ExecutionSummaryPluginConfiguration _configuration = new();
    private static readonly string _filePathOptionName = "--summary-file-path";
    private static readonly string _groupByOptionName = "--summary-group-by";
    private const string _requestsInterceptedMessage = "Requests intercepted";
    private const string _requestsPassedThroughMessage = "Requests passed through";

    public override Option[] GetOptions()
    {
        var filePath = new Option<string?>(_filePathOptionName, "Path to the file where the summary should be saved. If not specified, the summary will be printed to the console. Path can be absolute or relative to the current working directory.")
        {
            ArgumentHelpName = "summary-file-path"
        };
        filePath.AddValidator(input =>
        {
            var outputFilePath = input.Tokens.First().Value;
            if (string.IsNullOrEmpty(outputFilePath))
            {
                return;
            }

            var dirName = Path.GetDirectoryName(outputFilePath);
            if (string.IsNullOrEmpty(dirName))
            {
                // current directory exists so no need to check
                return;
            }

            var outputDir = Path.GetFullPath(dirName);
            if (!Directory.Exists(outputDir))
            {
                input.ErrorMessage = $"The directory {outputDir} does not exist.";
            }
        });

        var groupBy = new Option<SummaryGroupBy?>(_groupByOptionName, "Specifies how the information should be grouped in the summary. Available options: `url` (default), `messageType`.")
        {
            ArgumentHelpName = "summary-group-by"
        };
        groupBy.AddValidator(input =>
        {
            if (!Enum.TryParse<SummaryGroupBy>(input.Tokens.First().Value, true, out var groupBy))
            {
                input.ErrorMessage = $"{input.Tokens.First().Value} is not a valid option to group by. Allowed values are: {string.Join(", ", Enum.GetNames(typeof(SummaryGroupBy)))}";
            }
        });

        return [filePath, groupBy];
    }

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        configSection?.Bind(_configuration);

        pluginEvents.OptionsLoaded += OnOptionsLoaded;
        pluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    private void OnOptionsLoaded(object? sender, OptionsLoadedArgs e)
    {
        InvocationContext context = e.Context;

        var filePath = context.ParseResult.GetValueForOption<string?>(_filePathOptionName, e.Options);
        if (filePath is not null)
        {
            _configuration.FilePath = filePath;
        }

        var groupBy = context.ParseResult.GetValueForOption<SummaryGroupBy?>(_groupByOptionName, e.Options);
        if (groupBy is not null)
        {
            _configuration.GroupBy = groupBy.Value;
        }
    }

    private Task AfterRecordingStop(object? sender, RecordingArgs e)
    {
        if (!e.RequestLogs.Any())
        {
            return Task.CompletedTask;
        }

        var report = _configuration.GroupBy switch
        {
            SummaryGroupBy.Url => GetGroupedByUrlReport(e.RequestLogs),
            SummaryGroupBy.MessageType => GetGroupedByMessageTypeReport(e.RequestLogs),
            _ => throw new NotImplementedException()
        };

        if (string.IsNullOrEmpty(_configuration.FilePath))
        {
            _logger?.LogInformation("Report:\r\n{report}", string.Join(Environment.NewLine, report));
        }
        else
        {
            File.WriteAllLines(_configuration.FilePath, report);
        }

        return Task.CompletedTask;
    }

    private string[] GetGroupedByUrlReport(IEnumerable<RequestLog> requestLogs)
    {
        var report = new List<string>();
        report.AddRange(GetReportTitle());
        report.Add("## Requests");

        var data = GetGroupedByUrlData(requestLogs);

        var sortedMethodAndUrls = data.Keys.OrderBy(k => k);
        foreach (var methodAndUrl in sortedMethodAndUrls)
        {
            report.AddRange(new[] {
        "",
        $"### {methodAndUrl}",
      });

            var sortedMessageTypes = data[methodAndUrl].Keys.OrderBy(k => k);
            foreach (var messageType in sortedMessageTypes)
            {
                report.AddRange(new[] {
          "",
          $"#### {messageType}",
          ""
        });

                var sortedMessages = data[methodAndUrl][messageType].Keys.OrderBy(k => k);
                foreach (var message in sortedMessages)
                {
                    report.Add($"- ({data[methodAndUrl][messageType][message]}) {message}");
                }
            }
        }

        report.AddRange(GetSummary(requestLogs));

        return report.ToArray();
    }

    private string[] GetGroupedByMessageTypeReport(IEnumerable<RequestLog> requestLogs)
    {
        var report = new List<string>();
        report.AddRange(GetReportTitle());
        report.Add("## Message types");

        var data = GetGroupedByMessageTypeData(requestLogs);

        var sortedMessageTypes = data.Keys.OrderBy(k => k);
        foreach (var messageType in sortedMessageTypes)
        {
            report.AddRange(new[] {
        "",
        $"### {messageType}"
      });

            if (messageType == _requestsInterceptedMessage ||
                messageType == _requestsPassedThroughMessage)
            {
                report.Add("");

                var sortedMethodAndUrls = data[messageType][messageType].Keys.OrderBy(k => k);
                foreach (var methodAndUrl in sortedMethodAndUrls)
                {
                    report.Add($"- ({data[messageType][messageType][methodAndUrl]}) {methodAndUrl}");
                }
            }
            else
            {
                var sortedMessages = data[messageType].Keys.OrderBy(k => k);
                foreach (var message in sortedMessages)
                {
                    report.AddRange(new[] {
            "",
            $"#### {message}",
            ""
          });

                    var sortedMethodAndUrls = data[messageType][message].Keys.OrderBy(k => k);
                    foreach (var methodAndUrl in sortedMethodAndUrls)
                    {
                        report.Add($"- ({data[messageType][message][methodAndUrl]}) {methodAndUrl}");
                    }
                }
            }
        }

        report.AddRange(GetSummary(requestLogs));

        return report.ToArray();
    }

    private string[] GetReportTitle()
    {
        return new string[]
        {
      "# Dev Proxy execution summary",
      "",
      $"Date: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}",
      ""
        };
    }

    // in this method we're producing the follow data structure
    // request > message type > (count) message
    private Dictionary<string, Dictionary<string, Dictionary<string, int>>> GetGroupedByUrlData(IEnumerable<RequestLog> requestLogs)
    {
        var data = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();

        foreach (var log in requestLogs)
        {
            var message = GetRequestMessage(log);
            if (log.MessageType == MessageType.InterceptedResponse)
            {
                // ignore intercepted response messages
                continue;
            }

            if (log.MessageType == MessageType.InterceptedRequest)
            {
                var request = GetMethodAndUrl(log);
                if (!data.ContainsKey(request))
                {
                    data.Add(request, new Dictionary<string, Dictionary<string, int>>());
                }

                continue;
            }

            // last line of the message is the method and URL of the request
            var methodAndUrl = GetMethodAndUrl(log);
            var readableMessageType = GetReadableMessageTypeForSummary(log.MessageType);
            if (!data[methodAndUrl].ContainsKey(readableMessageType))
            {
                data[methodAndUrl].Add(readableMessageType, new Dictionary<string, int>());
            }

            if (data[methodAndUrl][readableMessageType].ContainsKey(message))
            {
                data[methodAndUrl][readableMessageType][message]++;
            }
            else
            {
                data[methodAndUrl][readableMessageType].Add(message, 1);
            }
        }

        return data;
    }

    // in this method we're producing the follow data structure
    // message type > message > (count) request
    private Dictionary<string, Dictionary<string, Dictionary<string, int>>> GetGroupedByMessageTypeData(IEnumerable<RequestLog> requestLogs)
    {
        var data = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();

        foreach (var log in requestLogs)
        {
            if (log.MessageType == MessageType.InterceptedResponse)
            {
                // ignore intercepted response messages
                continue;
            }

            var readableMessageType = GetReadableMessageTypeForSummary(log.MessageType);
            if (!data.ContainsKey(readableMessageType))
            {
                data.Add(readableMessageType, new Dictionary<string, Dictionary<string, int>>());

                if (log.MessageType == MessageType.InterceptedRequest ||
                    log.MessageType == MessageType.PassedThrough)
                {
                    // intercepted and passed through requests don't have
                    // a sub-grouping so let's repeat the message type
                    // to keep the same data shape
                    data[readableMessageType].Add(readableMessageType, new Dictionary<string, int>());
                }
            }

            var message = GetRequestMessage(log);
            if (log.MessageType == MessageType.InterceptedRequest ||
                log.MessageType == MessageType.PassedThrough)
            {
                // for passed through requests we need to log the URL rather than the
                // fixed message
                if (log.MessageType == MessageType.PassedThrough)
                {
                    message = GetMethodAndUrl(log);
                }

                if (!data[readableMessageType][readableMessageType].ContainsKey(message))
                {
                    data[readableMessageType][readableMessageType].Add(message, 1);
                }
                else
                {
                    data[readableMessageType][readableMessageType][message]++;
                }
                continue;
            }

            if (!data[readableMessageType].ContainsKey(message))
            {
                data[readableMessageType].Add(message, new Dictionary<string, int>());
            }
            var methodAndUrl = GetMethodAndUrl(log);
            if (data[readableMessageType][message].ContainsKey(methodAndUrl))
            {
                data[readableMessageType][message][methodAndUrl]++;
            }
            else
            {
                data[readableMessageType][message].Add(methodAndUrl, 1);
            }
        }

        return data;
    }

    private string GetRequestMessage(RequestLog requestLog)
    {
        return String.Join(' ', requestLog.MessageLines);
    }

    private string GetMethodAndUrl(RequestLog requestLog)
    {
        if (requestLog.Context is not null)
        {
            return $"{requestLog.Context.Session.HttpClient.Request.Method} {requestLog.Context.Session.HttpClient.Request.RequestUri}";
        }
        else
        {
            return "Undefined";
        }
    }

    private string[] GetSummary(IEnumerable<RequestLog> requestLogs)
    {
        var data = requestLogs
          .Where(log => log.MessageType != MessageType.InterceptedResponse)
          .Select(log => GetReadableMessageTypeForSummary(log.MessageType))
          .OrderBy(log => log)
          .GroupBy(log => log)
          .ToDictionary(group => group.Key, group => group.Count());

        var summary = new List<string> {
        "",
        "## Summary",
        "",
        "Category|Count",
        "--------|----:"
    };

        foreach (var messageType in data.Keys)
        {
            summary.Add($"{messageType}|{data[messageType]}");
        }

        return summary.ToArray();
    }

    private string GetReadableMessageTypeForSummary(MessageType messageType) => messageType switch
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
