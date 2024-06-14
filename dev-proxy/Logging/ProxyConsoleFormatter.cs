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
    private Dictionary<int, List<RequestLog>> _requestLogs = [];
    private ConsoleFormatterOptions _options;
    const string labelSpacing = " ";
    // label length + 2
    private readonly static string noLabelSpacing = new string(' ', 4 + 2);

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

        WriteMessageBoxedWithInvertedLabels(message, logLevel, scopeProvider, textWriter);

        if (logEntry.Exception is not null)
        {
            textWriter.Write($" Exception Details: {logEntry.Exception}");
        }

        textWriter.WriteLine();
    }

    private void WriteMessageBoxedWithInvertedLabels(string? message, LogLevel logLevel, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        if (message is null)
        {
            return;
        }

        var label = GetLogLevelString(logLevel);
        var (bgColor, fgColor) = GetLogLevelColor(logLevel);

        textWriter.WriteColoredMessage($" {label} ", bgColor, fgColor);
        textWriter.Write($"{labelSpacing}{_boxSpacing}{(logLevel == LogLevel.Debug ? $"[{DateTime.Now:T}] " : "")}");

        if (_options.IncludeScopes && scopeProvider is not null)
        {
            scopeProvider.ForEachScope((scope, state) =>
            {
                if (scope is null)
                {
                    return;
                }

                if (scope is string scopeString)
                {
                    textWriter.Write(scopeString);
                    textWriter.Write(": ");
                }
                else if (scope.GetType().Name == "FormattedLogValues")
                {
                    textWriter.Write(scope.ToString());
                    textWriter.Write(": ");
                }
            }, textWriter);
        }

        textWriter.Write(message);
    }

    private void WriteLogMessageBoxedWithInvertedLabels(string[] message, MessageType messageType, TextWriter textWriter, bool lastMessage = false)
    {
        var label = GetMessageTypeString(messageType);
        var (bgColor, fgColor) = GetMessageTypeColor(messageType);

        switch (messageType)
        {
            case MessageType.InterceptedRequest:
                // always one line (method + URL)
                // print label and top of the box
                textWriter.WriteLine();
                textWriter.WriteColoredMessage($" {label} ", bgColor, fgColor);
                textWriter.WriteLine($"{(label.Length < 4 ? " " : "")}{labelSpacing}{_boxTopLeft}{message[0]}");
                break;
            default:
                if (message.Length == 1)
                {
                    textWriter.WriteColoredMessage($" {label} ", bgColor, fgColor);
                    textWriter.WriteLine($"{(label.Length < 4 ? " " : "")}{labelSpacing}{(lastMessage ? _boxBottomLeft : _boxLeft)}{message[0]}");
                }
                else
                {
                    for (var i = 0; i < message.Length; i++)
                    {
                        if (i == 0)
                        {
                            // print label and middle of the box
                            textWriter.WriteColoredMessage($" {label} ", bgColor, fgColor);
                            textWriter.WriteLine($"{(label.Length < 4 ? " " : "")}{labelSpacing}{_boxLeft}{message[i]}");
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

    // from https://github.com/dotnet/runtime/blob/198a2596229f69b8e02902bfb4ffc2a30be3b339/src/libraries/Microsoft.Extensions.Logging.Console/src/SimpleConsoleFormatter.cs#L154
    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }

    private static (ConsoleColor bg, ConsoleColor fg) GetLogLevelColor(LogLevel logLevel)
    {
        var fgColor = Console.ForegroundColor;
        var bgColor = Console.BackgroundColor;

        return logLevel switch
        {
            LogLevel.Information => (bgColor, ConsoleColor.Blue),
            LogLevel.Warning => (ConsoleColor.DarkYellow, fgColor),
            LogLevel.Error => (ConsoleColor.DarkRed, fgColor),
            LogLevel.Debug => (bgColor, ConsoleColor.Gray),
            LogLevel.Trace => (bgColor, ConsoleColor.Gray),
            _ => (bgColor, fgColor)
        };
    }

    private static string GetMessageTypeString(MessageType messageType)
    {
        return messageType switch
        {
            MessageType.InterceptedRequest => "req",
            MessageType.InterceptedResponse => "res",
            MessageType.PassedThrough => "api",
            MessageType.Chaos => "oops",
            MessageType.Warning => "warn",
            MessageType.Mocked => "mock",
            MessageType.Failed => "fail",
            MessageType.Tip => "tip",
            _ => "    "
        };
    }

    private static (ConsoleColor bg, ConsoleColor fg) GetMessageTypeColor(MessageType messageType)
    {
        var fgColor = Console.ForegroundColor;
        var bgColor = Console.BackgroundColor;

        return messageType switch
        {
            MessageType.InterceptedRequest => (bgColor, ConsoleColor.Gray),
            MessageType.PassedThrough => (ConsoleColor.Gray, fgColor),
            MessageType.Chaos => (ConsoleColor.DarkRed, fgColor),
            MessageType.Warning => (ConsoleColor.DarkYellow, fgColor),
            MessageType.Mocked => (ConsoleColor.DarkMagenta, fgColor),
            MessageType.Failed => (ConsoleColor.DarkRed, fgColor),
            MessageType.Tip => (ConsoleColor.DarkBlue, fgColor),
            _ => (bgColor, fgColor)
        };
    }
}