// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Microsoft.DevProxy.Logging;

public class ProxyConsoleFormatter : ConsoleFormatter
{
    private const string _boxTopLeft = "\u256d ";
    private const string _boxLeft = "\u2502 ";
    private const string _boxBottomLeft = "\u2570 ";
    // used to align single-line messages
    private const string _boxSpacing = "  ";
    private Dictionary<int, List<RequestLog>> _requestLogs = new();
    private ConsoleFormatterOptions _options;
    const string labelSpacing = "  ";
    const string interceptedRequest = "request";
    const string passedThrough = "api";
    const string chaos = "chaos";
    const string warning = "warning";
    const string mock = "mock";
    const string normal = "log";
    const string fail = "fail";
    const string tip = "tip";
    const string error = "error";
    const string info = "info";
    private readonly static string[] allLabels = [interceptedRequest, passedThrough, chaos, warning, mock, normal, fail, tip];
    private readonly static int maxLabelLength = allLabels.Max(l => l.Length);
    private readonly static string noLabelSpacing = new string(' ', maxLabelLength + 2);

    public ProxyConsoleFormatter(IOptions<ConsoleFormatterOptions> options) : base("devproxy")
    {
        // needed to properly required rounded corners in the box
        Console.OutputEncoding = Encoding.UTF8;
        _options = options.Value;
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        if (logEntry.State is RequestLog requestLog)
        {
            LogRequest(requestLog, scopeProvider, textWriter);
        }
        else
        {
            LogMessage(logEntry, scopeProvider, textWriter);
        }
    }

    private void LogRequest(RequestLog requestLog, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var messageType = requestLog.MessageType;

        // don't log intercepted response to console
        if (messageType == MessageType.InterceptedResponse)
        {
            return;
        }

        var requestId = GetRequestIdScope(scopeProvider);

        if (requestId is not null)
        {
            if (messageType == MessageType.FinishedProcessingRequest)
            {
                var lastMessage = _requestLogs[requestId.Value].Last();
                // log all request logs for the request
                foreach (var log in _requestLogs[requestId.Value])
                {
                    WriteLogMessageBoxedWithInvertedLabels(log.MessageLines, log.MessageType, textWriter, log == lastMessage);
                }
                _requestLogs.Remove(requestId.Value);
            }
            else
            {
                // buffer request logs until the request is finished processing
                if (!_requestLogs.ContainsKey(requestId.Value))
                {
                    _requestLogs[requestId.Value] = new();
                }
                _requestLogs[requestId.Value].Add(requestLog);
            }
        }
    }

    private int? GetRequestIdScope(IExternalScopeProvider? scopeProvider)
    {
        int? requestId = null;

        scopeProvider?.ForEachScope((scope, state) =>
        {
            if (scope is Dictionary<string, object> dictionary)
            {
                if (dictionary.TryGetValue(nameof(requestId), out var req))
                {
                    requestId = (int)req;
                }
            }
        }, "");

        return requestId;
    }

    private void LogMessage<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        // regular messages
        var logLevel = logEntry.LogLevel;
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);

        WriteMessageBoxedWithInvertedLabels(message, logLevel, textWriter);

        if (logEntry.Exception is not null)
        {
            textWriter.Write($" Exception Details: {logEntry.Exception}");
        }

        if (_options.IncludeScopes && scopeProvider is not null)
        {
            scopeProvider.ForEachScope((scope, state) =>
            {
                state.Write(" => ");
                state.Write(scope);
            }, textWriter);
        }
        textWriter.WriteLine();
    }

    private void WriteMessageBoxedWithInvertedLabels(string? message, LogLevel logLevel, TextWriter textWriter)
    {
        var label = normal;
        var fgColor = Console.ForegroundColor;
        var bgColor = Console.BackgroundColor;

        switch (logLevel)
        {
            case LogLevel.Information:
                label = info;
                fgColor = ConsoleColor.Blue;
                break;
            case LogLevel.Warning:
                label = warning;
                bgColor = ConsoleColor.DarkYellow;
                break;
            case LogLevel.Error:
                label = error;
                bgColor = ConsoleColor.DarkRed;
                break;
            case LogLevel.Debug:
                label = "debug";
                fgColor = ConsoleColor.Gray;
                break;
        }

        var leadingSpaces = new string(' ', maxLabelLength - label.Length);

        if (message is not null)
        {
            textWriter.Write(leadingSpaces);
            textWriter.WriteColoredMessage($" {label} ", bgColor, fgColor);
            textWriter.Write($"{labelSpacing}{_boxSpacing}{message}");
        }
    }

    private void WriteLogMessageBoxedWithInvertedLabels(string[] message, MessageType messageType, TextWriter textWriter, bool lastMessage = false)
    {
        var label = normal;
        var fgColor = Console.ForegroundColor;
        var bgColor = Console.BackgroundColor;

        switch (messageType)
        {
            case MessageType.InterceptedRequest:
                label = interceptedRequest;
                fgColor = ConsoleColor.Gray;
                break;
            case MessageType.PassedThrough:
                label = passedThrough;
                bgColor = ConsoleColor.Gray;
                break;
            case MessageType.Chaos:
                label = chaos;
                bgColor = ConsoleColor.DarkRed;
                break;
            case MessageType.Warning:
                label = warning;
                bgColor = ConsoleColor.DarkYellow;
                break;
            case MessageType.Mocked:
                label = mock;
                bgColor = ConsoleColor.DarkMagenta;
                break;
            case MessageType.Failed:
                label = fail;
                bgColor = ConsoleColor.DarkRed;
                break;
            case MessageType.Tip:
                label = tip;
                bgColor = ConsoleColor.DarkBlue;
                break;
            case MessageType.Normal:
                label = normal;
                break;
        }

        var leadingSpaces = new string(' ', maxLabelLength - label.Length);

        switch (messageType)
        {
            case MessageType.InterceptedRequest:
                // always one line (method + URL)
                // print label and top of the box
                textWriter.WriteLine();
                textWriter.Write(leadingSpaces);
                textWriter.WriteColoredMessage($" {label} ", bgColor, fgColor);
                textWriter.WriteLine($"{labelSpacing}{_boxTopLeft}{message[0]}");
                break;
            default:
                if (message.Length == 1)
                {
                    textWriter.Write(leadingSpaces);
                    textWriter.WriteColoredMessage($" {label} ", bgColor, fgColor);
                    textWriter.WriteLine($"{labelSpacing}{(lastMessage ? _boxBottomLeft : _boxLeft)}{message[0]}");
                }
                else
                {
                    for (var i = 0; i < message.Length; i++)
                    {
                        if (i == 0)
                        {
                            // print label and middle of the box
                            textWriter.Write(leadingSpaces);
                            textWriter.WriteColoredMessage($" {label} ", bgColor, fgColor);
                            textWriter.WriteLine($"{labelSpacing}{_boxLeft}{message[i]}");
                        }
                        else if (i < message.Length - 1)
                        {
                            // print middle of the box
                            textWriter.WriteLine($"{noLabelSpacing}{labelSpacing}{_boxLeft}{message[i]}");
                        }
                        else
                        {
                            // print end of the box
                            textWriter.WriteLine($"{noLabelSpacing}{labelSpacing}{(lastMessage ? _boxBottomLeft : _boxLeft)}{message[i]}");
                        }
                    }
                }
                break;
        }
    }
}