// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy;

public class ConsoleLogger : IProxyLogger
{
    private readonly ConsoleColor _color;
    private readonly LabelMode _labelMode;
    private readonly PluginEvents _pluginEvents;
    private const string _boxTopLeft = "\u256d ";
    private const string _boxLeft = "\u2502 ";
    private const string _boxBottomLeft = "\u2570 ";
    // used to align single-line messages
    private const string _boxSpacing = "  ";

    public static readonly object ConsoleLock = new object();

    public LogLevel LogLevel { get; set; }

    public ConsoleLogger(ProxyConfiguration configuration, PluginEvents pluginEvents)
    {
        // needed to properly required rounded corners in the box
        Console.OutputEncoding = Encoding.UTF8;
        _color = Console.ForegroundColor;
        _labelMode = configuration.LabelMode;
        _pluginEvents = pluginEvents;
        LogLevel = configuration.LogLevel;
    }

    private void WriteLog(string message)
    {
        Console.WriteLine(message);
    }

    private void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"  WARNING: {message}");
        Console.ForegroundColor = _color;
    }

    private void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = _color;
    }

    private void WriteDebug(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = _color;
    }

    public void LogRequest(string[] message, MessageType messageType, LoggingContext? context = null)
    {
        var method = context?.Session.HttpClient.Request.Method;
        var url = context?.Session.HttpClient.Request.Url;
        LogRequest(message, messageType, method, url, context);
    }

    public void LogRequest(string[] message, MessageType messageType, string method, string url)
    {
        LogRequest(message, messageType, method, url, null);
    }

    private void LogRequest(string[] message, MessageType messageType, string? method, string? url, LoggingContext? context = null)
    {
        var messageLines = new List<string>(message);

        // don't log intercepted response to console
        if (messageType != MessageType.InterceptedResponse)
        {
            // add request context information to the message for messages
            // that are not intercepted requests and have a context
            if (messageType != MessageType.InterceptedRequest &&
                method is not null &&
                url is not null)
            {
                messageLines.Add($"{method} {url}");
            }

            lock (ConsoleLock)
            {
                switch (_labelMode)
                {
                    case LabelMode.Text:
                        WriteBoxedWithInvertedLabels(messageLines.ToArray(), messageType);
                        break;
                    case LabelMode.Icon:
                        WriteBoxedWithAsciiIcons(messageLines.ToArray(), messageType);
                        break;
                    case LabelMode.NerdFont:
                        WriteBoxedWithNerdFontIcons(messageLines.ToArray(), messageType);
                        break;
                }
            }
        }

        _pluginEvents.RaiseRequestLogged(new RequestLogArgs(new RequestLog(message, messageType, context)));
    }

    public void WriteBoxedWithInvertedLabels(string[] message, MessageType messageType)
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
            Console.Write(leadingSpaces);
            Console.ForegroundColor = fgColor;
            Console.BackgroundColor = bgColor;
            Console.Write($" {label} ");
            Console.ResetColor();
            Console.WriteLine($"{labelSpacing}{_boxSpacing}{message[0]}");
        }
        else
        {
            for (var i = 0; i < message.Length; i++)
            {
                if (i == 0)
                {
                    // print label and top of the box
                    Console.Write(leadingSpaces);
                    Console.ForegroundColor = fgColor;
                    Console.BackgroundColor = bgColor;
                    Console.Write($" {label} ");
                    Console.ResetColor();
                    Console.WriteLine($"{labelSpacing}{_boxTopLeft}{message[i]}");
                }
                else if (i < message.Length - 1)
                {
                    // print middle of the box
                    Console.WriteLine($"{noLabelSpacing}{labelSpacing}{_boxLeft}{message[i]}");
                }
                else
                {
                    // print end of the box
                    Console.WriteLine($"{noLabelSpacing}{labelSpacing}{_boxBottomLeft}{message[i]}");
                }
            }
        }
    }

    public void WriteBoxedWithAsciiIcons(string[] message, MessageType messageType)
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

        Console.ForegroundColor = fgColor;

        if (message.Length == 1)
        {
            // no need to box a single line message
            Console.WriteLine($"{icon}{iconSpacing}{_boxSpacing}{message[0]}");
        }
        else
        {
            for (var i = 0; i < message.Length; i++)
            {
                if (i == 0)
                {
                    // print label and top of the box
                    Console.WriteLine($"{icon}{iconSpacing}{_boxTopLeft}{message[i]}");
                }
                else if (i < message.Length - 1)
                {
                    // print middle of the box
                    Console.WriteLine($"{noIconSpacing}{iconSpacing}{_boxLeft}{message[i]}");
                }
                else
                {
                    // print end of the box
                    Console.WriteLine($"{noIconSpacing}{iconSpacing}{_boxBottomLeft}{message[i]}");
                }
            }
        }

        Console.ResetColor();
    }

    public void WriteBoxedWithNerdFontIcons(string[] message, MessageType messageType)
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

        Console.ForegroundColor = fgColor;

        if (message.Length == 1)
        {
            // no need to box a single line message
            Console.WriteLine($"{icon}{iconSpacing}{_boxSpacing}{message[0]}");
        }
        else
        {
            for (var i = 0; i < message.Length; i++)
            {
                if (i == 0)
                {
                    // print label and top of the box
                    Console.WriteLine($"{icon}{iconSpacing}{_boxTopLeft}{message[i]}");
                }
                else if (i < message.Length - 1)
                {
                    // print middle of the box
                    Console.WriteLine($"{noIconSpacing}{iconSpacing}{_boxLeft}{message[i]}");
                }
                else
                {
                    // print end of the box
                    Console.WriteLine($"{noIconSpacing}{iconSpacing}{_boxBottomLeft}{message[i]}");
                }
            }
        }

        Console.ResetColor();
    }

    public object Clone()
    {
        return new ConsoleLogger(new ProxyConfiguration
        {
            LabelMode = _labelMode,
            LogLevel = LogLevel
        }, _pluginEvents);
    }

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        // temporary fix to log exceptions
        // long term we should move this to the formatter
        if (exception is not null)
        {
            message += Environment.NewLine + exception;
        }
        message = message.ReplaceLineEndings();
        switch (logLevel)
        {
            case LogLevel.Debug:
                WriteDebug(message);
                break;
            case LogLevel.Information:
                WriteLog(message);
                break;
            case LogLevel.Warning:
                WriteWarning(message);
                break;
            case LogLevel.Error:
                WriteError(message);
                break;
        }
        
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevelToTest) => logLevelToTest >= LogLevel; // only log if the log level is greater than or equal to the current log level

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;
}