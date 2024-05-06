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
    private ProxyConsoleFormatterOptions _options { get; }
    private const string _boxTopLeft = "\u256d ";
    private const string _boxLeft = "\u2502 ";
    private const string _boxBottomLeft = "\u2570 ";
    // used to align single-line messages
    private const string _boxSpacing = "  ";
    const string _defaultForegroundColor = "\x1B[39m\x1B[22m";
    const string _defaultBackgroundColor = "\x1B[49m";


    public ProxyConsoleFormatter(IOptions<ProxyConsoleFormatterOptions> options) : base("devproxy")
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
        var (message, messageType, _, method, url) = requestLog;

        // don't log intercepted response to console
        if (messageType == MessageType.InterceptedResponse)
        {
            return;
        }

        var messageLines = new List<string>(message);

        // add request context information to the message for messages
        // that are not intercepted requests and have a context
        if (messageType != MessageType.InterceptedRequest &&
            method is not null &&
            url is not null)
        {
            messageLines.Add($"{method} {url}");
        }

        Action<string[], MessageType, TextWriter> writer = _options.LabelMode switch
        {
            LabelMode.Text => WriteBoxedWithInvertedLabels,
            LabelMode.Icon => WriteBoxedWithAsciiIcons,
            LabelMode.NerdFont => WriteBoxedWithNerdFontIcons,
            _ => WriteBoxedWithInvertedLabels
        };

        writer(messageLines.ToArray(), messageType, textWriter);
    }

    private void LogMessage<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        // regular messages
        var logLevel = logEntry.LogLevel;
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        var timestamp = DateTime.Now.ToString();

        Action<string?, TextWriter> writer = logLevel switch
        {
            LogLevel.Information => WriteInformation,
            LogLevel.Warning => WriteWarning,
            LogLevel.Error => WriteError,
            LogLevel.Debug => WriteDebug,
            LogLevel.Trace => WriteInformation,
            LogLevel.Critical => WriteInformation,
            LogLevel.None => WriteInformation,
            _ => WriteInformation
        };
        writer(message, textWriter);
        
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

    private void WriteInformation(string? message, TextWriter writer)
    {
        writer.Write(message);
    }

    private void WriteWarning(string? message, TextWriter writer)
    {
        writer.Write(GetForegroundColorEscapeCode(ConsoleColor.Yellow));
        writer.Write($"  WARNING: {message}");
        writer.ResetColor();
    }

    private void WriteError(string? message, TextWriter writer)
    {
        writer.Write(GetForegroundColorEscapeCode(ConsoleColor.Red));
        writer.Write(message);
        writer.ResetColor();
    }

    private void WriteDebug(string? message, TextWriter writer)
    {
        writer.Write(GetForegroundColorEscapeCode(ConsoleColor.Gray));
        writer.Write($"[{DateTime.Now}] {message}");
        writer.ResetColor();   
    }

    private void WriteBoxedWithInvertedLabels(string[] message, MessageType messageType, TextWriter textWriter)
    {
        const string labelSpacing = "  ";
        const string interceptedRequest = "request";
        const string passedThrough = "api";
        const string chaos = "chaos";
        const string warning = "warning";
        const string mock = "mock";
        const string normal = "log";
        const string fail = "fail";
        const string tip = "tip";
        var allLabels = new[] { interceptedRequest, passedThrough, chaos, warning, mock, normal, fail, tip };
        var maxLabelLength = allLabels.Max(l => l.Length);
        var noLabelSpacing = new string(' ', maxLabelLength + 2);

        var label = normal;
        var fgColor = Console.ForegroundColor;
        var bgColor = Console.BackgroundColor;

        switch (messageType)
        {
            case MessageType.InterceptedRequest:
                label = interceptedRequest;
                bgColor = ConsoleColor.DarkGray;
                break;
            case MessageType.PassedThrough:
                label = passedThrough;
                fgColor = ConsoleColor.Black;
                bgColor = ConsoleColor.Gray;
                break;
            case MessageType.Chaos:
                label = chaos;
                fgColor = ConsoleColor.White;
                bgColor = ConsoleColor.DarkRed;
                break;
            case MessageType.Warning:
                label = warning;
                fgColor = ConsoleColor.Black;
                bgColor = ConsoleColor.Yellow;
                break;
            case MessageType.Mocked:
                label = mock;
                fgColor = ConsoleColor.Black;
                bgColor = ConsoleColor.DarkYellow;
                break;
            case MessageType.Failed:
                label = fail;
                fgColor = ConsoleColor.Black;
                bgColor = ConsoleColor.Red;
                break;
            case MessageType.Tip:
                label = tip;
                fgColor = ConsoleColor.White;
                bgColor = ConsoleColor.Blue;
                break;
            case MessageType.Normal:
                label = normal;
                break;
        }

        var leadingSpaces = new string(' ', maxLabelLength - label.Length);

        if (message.Length == 1)
        {
            // no need to box a single line message
            textWriter.Write(leadingSpaces);
            textWriter.Write(GetForegroundColorEscapeCode(fgColor));
            textWriter.Write(GetBackgroundColorEscapeCode(bgColor));
            textWriter.Write($" {label} ");
            textWriter.ResetColor();
            textWriter.WriteLine($"{labelSpacing}{_boxSpacing}{message[0]}");
        }
        else
        {
            for (var i = 0; i < message.Length; i++)
            {
                if (i == 0)
                {
                    // print label and top of the box
                    textWriter.Write(leadingSpaces);
                    textWriter.Write(GetForegroundColorEscapeCode(fgColor));
                    textWriter.Write(GetBackgroundColorEscapeCode(bgColor));
                    textWriter.Write($" {label} ");
                    textWriter.ResetColor();
                    textWriter.WriteLine($"{labelSpacing}{_boxTopLeft}{message[i]}");
                }
                else if (i < message.Length - 1)
                {
                    // print middle of the box
                    textWriter.WriteLine($"{noLabelSpacing}{labelSpacing}{_boxLeft}{message[i]}");
                }
                else
                {
                    // print end of the box
                    textWriter.WriteLine($"{noLabelSpacing}{labelSpacing}{_boxBottomLeft}{message[i]}");
                }
            }
        }
    }

    private void WriteBoxedWithAsciiIcons(string[] message, MessageType messageType, TextWriter textWriter)
    {
        const string iconSpacing = "  ";
        const string noIconSpacing = "   ";

        // Set the icon based on the provided message type, using this switch statement
        var icon = messageType switch
        {
            MessageType.InterceptedRequest => "← ←",
            MessageType.PassedThrough => "↑ ↑",
            MessageType.Chaos => "× →",
            MessageType.Warning => "/!\\",
            MessageType.Mocked => "o →",
            MessageType.Failed => "! →",
            MessageType.Tip => "(i)",
            MessageType.Normal => "   ",
            _ => "   "
        };
        // Set the foreground color based on the provided message type, using this switch statement
        var fgColor = messageType switch
        {
            MessageType.PassedThrough => ConsoleColor.Gray,
            MessageType.Chaos => ConsoleColor.DarkRed,
            MessageType.Warning => ConsoleColor.Yellow,
            MessageType.Mocked => ConsoleColor.DarkYellow,
            MessageType.Failed => ConsoleColor.Red,
            MessageType.Tip => ConsoleColor.Blue,
            _ => Console.ForegroundColor
        };

        textWriter.Write(GetForegroundColorEscapeCode(fgColor));

        if (message.Length == 1)
        {
            // no need to box a single line message
            textWriter.WriteLine($"{icon}{iconSpacing}{_boxSpacing}{message[0]}");
        }
        else
        {
            for (var i = 0; i < message.Length; i++)
            {
                if (i == 0)
                {
                    // print label and top of the box
                    textWriter.WriteLine($"{icon}{iconSpacing}{_boxTopLeft}{message[i]}");
                }
                else if (i < message.Length - 1)
                {
                    // print middle of the box
                    textWriter.WriteLine($"{noIconSpacing}{iconSpacing}{_boxLeft}{message[i]}");
                }
                else
                {
                    // print end of the box
                    textWriter.WriteLine($"{noIconSpacing}{iconSpacing}{_boxBottomLeft}{message[i]}");
                }
            }
        }

        textWriter.ResetColor();
    }

    private void WriteBoxedWithNerdFontIcons(string[] message, MessageType messageType, TextWriter textWriter)
    {
        const string iconSpacing = "  ";
        const string noIconSpacing = " ";

        // Set the icon based on the provided message type, using this switch statement
        var icon = messageType switch
        {
            MessageType.InterceptedRequest => "\uf441",
            MessageType.PassedThrough => "\ue33c",
            MessageType.Chaos => "\uf188",
            MessageType.Warning => "\uf421",
            MessageType.Mocked => "\uf064",
            MessageType.Failed => "\uf65b",
            MessageType.Tip => "\ufbe6",
            _ => " "
        };
        // Set the foreground color based on the provided message type, using this switch statement
        var fgColor = messageType switch
        {
            MessageType.PassedThrough => ConsoleColor.Gray,
            MessageType.Chaos => ConsoleColor.DarkRed,
            MessageType.Warning => ConsoleColor.Yellow,
            MessageType.Mocked => ConsoleColor.DarkYellow,
            MessageType.Failed => ConsoleColor.Red,
            MessageType.Tip => ConsoleColor.Blue,
            _ => Console.ForegroundColor
        };

        textWriter.Write(GetForegroundColorEscapeCode(fgColor));

        if (message.Length == 1)
        {
            // no need to box a single line message
            textWriter.WriteLine($"{icon}{iconSpacing}{_boxSpacing}{message[0]}");
        }
        else
        {
            for (var i = 0; i < message.Length; i++)
            {
                if (i == 0)
                {
                    // print label and top of the box
                    textWriter.WriteLine($"{icon}{iconSpacing}{_boxTopLeft}{message[i]}");
                }
                else if (i < message.Length - 1)
                {
                    // print middle of the box
                    textWriter.WriteLine($"{noIconSpacing}{iconSpacing}{_boxLeft}{message[i]}");
                }
                else
                {
                    // print end of the box
                    textWriter.WriteLine($"{noIconSpacing}{iconSpacing}{_boxBottomLeft}{message[i]}");
                }
            }
        }

        textWriter.ResetColor();
    }

    static string GetForegroundColorEscapeCode(ConsoleColor color) =>
        color switch
        {
            ConsoleColor.Black => "\x1B[30m",
            ConsoleColor.DarkRed => "\x1B[31m",
            ConsoleColor.DarkGreen => "\x1B[32m",
            ConsoleColor.DarkYellow => "\x1B[33m",
            ConsoleColor.DarkBlue => "\x1B[34m",
            ConsoleColor.DarkMagenta => "\x1B[35m",
            ConsoleColor.DarkCyan => "\x1B[36m",
            ConsoleColor.Gray => "\x1B[37m",
            ConsoleColor.Red => "\x1B[1m\x1B[31m",
            ConsoleColor.Green => "\x1B[1m\x1B[32m",
            ConsoleColor.Yellow => "\x1B[1m\x1B[33m",
            ConsoleColor.Blue => "\x1B[1m\x1B[34m",
            ConsoleColor.Magenta => "\x1B[1m\x1B[35m",
            ConsoleColor.Cyan => "\x1B[1m\x1B[36m",
            ConsoleColor.White => "\x1B[1m\x1B[37m",

            _ => _defaultForegroundColor
        };

    static string GetBackgroundColorEscapeCode(ConsoleColor color) =>
        color switch
        {
            ConsoleColor.Black => "\x1B[40m",
            ConsoleColor.DarkRed => "\x1B[41m",
            ConsoleColor.DarkGreen => "\x1B[42m",
            ConsoleColor.DarkYellow => "\x1B[43m",
            ConsoleColor.DarkBlue => "\x1B[44m",
            ConsoleColor.DarkMagenta => "\x1B[45m",
            ConsoleColor.DarkCyan => "\x1B[46m",
            ConsoleColor.Gray => "\x1B[47m",

            _ => _defaultBackgroundColor
        };
}