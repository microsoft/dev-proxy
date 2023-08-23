// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Microsoft365.DeveloperProxy.Abstractions;

namespace Microsoft365.DeveloperProxy;

public class ConsoleLogger : ILogger {
    private readonly ConsoleColor _color;
    private readonly LabelMode _labelMode;
    private readonly PluginEvents _pluginEvents;
    private readonly string _boxTopLeft = "\u256d ";
    private readonly string _boxLeft = "\u2502 ";
    private readonly string _boxBottomLeft = "\u2570 ";
    // used to align single-line messages
    private readonly string _boxSpacing = "  ";

    public static readonly object ConsoleLock = new object();

    public LogLevel LogLevel { get; set; }

    public ConsoleLogger(ProxyConfiguration configuration, PluginEvents pluginEvents) {
        // needed to properly required rounded corners in the box
        Console.OutputEncoding = Encoding.UTF8;
        _color = Console.ForegroundColor;
        _labelMode = configuration.LabelMode;
        _pluginEvents = pluginEvents;
        LogLevel = configuration.LogLevel;
    }

    public void LogInfo(string message) {
        if (LogLevel > LogLevel.Info) {
            return;
        }

        Console.WriteLine(message);
    }

    public void LogWarn(string message) {
        if (LogLevel > LogLevel.Warn) {
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"  WARNING: {message}");
        Console.ForegroundColor = _color;
    }

    public void LogError(string message) {
        if (LogLevel > LogLevel.Error) {
            return;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = _color;
    }

    public void LogDebug(string message) {
        if (LogLevel > LogLevel.Debug) {
            return;
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = _color;
    }

    public void LogRequest(string[] message, MessageType messageType, LoggingContext? context = null) {
        var messageLines = new List<string>(message);

        // add request context information to the message for messages
        // that are not intercepted requests and have a context
        if (messageType != MessageType.InterceptedRequest &&
            context is not null) {
            messageLines.Add($"{context.Session.HttpClient.Request.Method} {context.Session.HttpClient.Request.Url}");
        }

        lock (ConsoleLock) {
            switch (_labelMode) {
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

        _pluginEvents.RaiseRequestLogged(new RequestLogArgs(new RequestLog(message, messageType, context)));
    }

    public void WriteBoxedWithInvertedLabels(string[] message, MessageType messageType) {
        var labelSpacing = "  ";
        var interceptedRequest = "request";
        var passedThrough = "api";
        var chaos = "chaos";
        var warning = "warning";
        var mock = "mock";
        var normal = "log";
        var fail = "fail";
        var tip = "tip";
        var allLabels = new[] { interceptedRequest, passedThrough, chaos, warning, mock, normal, fail, tip };
        var maxLabelLength = allLabels.Max(l => l.Length);
        var noLabelSpacing = new string(' ', maxLabelLength + 2);

        var label = normal;
        var fgColor = Console.ForegroundColor;
        var bgColor = Console.BackgroundColor;

        switch (messageType) {
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

        if (message.Length == 1) {
            // no need to box a single line message
            Console.Write(leadingSpaces);
            Console.ForegroundColor = fgColor;
            Console.BackgroundColor = bgColor;
            Console.Write($" {label} ");
            Console.ResetColor();
            Console.WriteLine($"{labelSpacing}{_boxSpacing}{message[0]}");
        }
        else {
            for (var i = 0; i < message.Length; i++) {
                if (i == 0) {
                    // print label and top of the box
                    Console.Write(leadingSpaces);
                    Console.ForegroundColor = fgColor;
                    Console.BackgroundColor = bgColor;
                    Console.Write($" {label} ");
                    Console.ResetColor();
                    Console.WriteLine($"{labelSpacing}{_boxTopLeft}{message[i]}");
                }
                else if (i < message.Length - 1) {
                    // print middle of the box
                    Console.WriteLine($"{noLabelSpacing}{labelSpacing}{_boxLeft}{message[i]}");
                }
                else {
                    // print end of the box
                    Console.WriteLine($"{noLabelSpacing}{labelSpacing}{_boxBottomLeft}{message[i]}");
                }
            }
        }
    }

    public void WriteBoxedWithAsciiIcons(string[] message, MessageType messageType) {
        var iconSpacing = "  ";
        var noIconSpacing = "   ";
        var interceptedRequest = $"← ←";
        var passedThrough = "↑ ↑";
        var chaos = "× →";
        var warning = "/!\\";
        var mock = "o →";
        var normal = "   ";
        var fail = "! →";
        var tip = "(i)";

        var icon = normal;
        var fgColor = Console.ForegroundColor;

        switch (messageType) {
            case MessageType.InterceptedRequest:
                icon = interceptedRequest;
                break;
            case MessageType.PassedThrough:
                icon = passedThrough;
                fgColor = ConsoleColor.Gray;
                break;
            case MessageType.Chaos:
                icon = chaos;
                fgColor = ConsoleColor.DarkRed;
                break;
            case MessageType.Warning:
                icon = warning;
                fgColor = ConsoleColor.Yellow;
                break;
            case MessageType.Mocked:
                icon = mock;
                fgColor = ConsoleColor.DarkYellow;
                break;
            case MessageType.Failed:
                icon = fail;
                fgColor = ConsoleColor.Red;
                break;
            case MessageType.Tip:
                icon = tip;
                fgColor = ConsoleColor.Blue;
                break;
            case MessageType.Normal:
                icon = normal;
                break;
        }
        
        Console.ForegroundColor = fgColor;

        if (message.Length == 1) {
            // no need to box a single line message
            Console.WriteLine($"{icon}{iconSpacing}{_boxSpacing}{message[0]}");
        }
        else {
            for (var i = 0; i < message.Length; i++) {
                if (i == 0) {
                    // print label and top of the box
                    Console.WriteLine($"{icon}{iconSpacing}{_boxTopLeft}{message[i]}");
                }
                else if (i < message.Length - 1) {
                    // print middle of the box
                    Console.WriteLine($"{noIconSpacing}{iconSpacing}{_boxLeft}{message[i]}");
                }
                else {
                    // print end of the box
                    Console.WriteLine($"{noIconSpacing}{iconSpacing}{_boxBottomLeft}{message[i]}");
                }
            }
        }

        Console.ResetColor();
    }

    public void WriteBoxedWithNerdFontIcons(string[] message, MessageType messageType) {
        var iconSpacing = "  ";
        var noIconSpacing = " ";
        var interceptedRequest = "\uf441";
        var passedThrough = "\ue33c";
        var chaos = "\uf188";
        var warning = "\uf421";
        var mock = "\uf064";
        var normal = " ";
        var fail = "\uf65b";
        var tip = "\ufbe6";

        var icon = normal;
        var fgColor = Console.ForegroundColor;

        switch (messageType) {
            case MessageType.InterceptedRequest:
                icon = interceptedRequest;
                break;
            case MessageType.PassedThrough:
                icon = passedThrough;
                fgColor = ConsoleColor.Gray;
                break;
            case MessageType.Chaos:
                icon = chaos;
                fgColor = ConsoleColor.DarkRed;
                break;
            case MessageType.Warning:
                icon = warning;
                fgColor = ConsoleColor.Yellow;
                break;
            case MessageType.Mocked:
                icon = mock;
                fgColor = ConsoleColor.DarkYellow;
                break;
            case MessageType.Failed:
                icon = fail;
                fgColor = ConsoleColor.Red;
                break;
            case MessageType.Tip:
                icon = tip;
                fgColor = ConsoleColor.Blue;
                break;
            case MessageType.Normal:
                icon = normal;
                break;
        }
        
        Console.ForegroundColor = fgColor;

        if (message.Length == 1) {
            // no need to box a single line message
            Console.WriteLine($"{icon}{iconSpacing}{_boxSpacing}{message[0]}");
        }
        else {
            for (var i = 0; i < message.Length; i++) {
                if (i == 0) {
                    // print label and top of the box
                    Console.WriteLine($"{icon}{iconSpacing}{_boxTopLeft}{message[i]}");
                }
                else if (i < message.Length - 1) {
                    // print middle of the box
                    Console.WriteLine($"{noIconSpacing}{iconSpacing}{_boxLeft}{message[i]}");
                }
                else {
                    // print end of the box
                    Console.WriteLine($"{noIconSpacing}{iconSpacing}{_boxBottomLeft}{message[i]}");
                }
            }
        }

        Console.ResetColor();
    }

    public object Clone()
    {
        return new ConsoleLogger(new ProxyConfiguration {
            LabelMode = _labelMode,
            LogLevel = LogLevel
        }, _pluginEvents);
    }
}