// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy.Abstractions;

namespace Microsoft.Graph.DeveloperProxy;

public class ConsoleLogger : ILogger {
    private readonly ConsoleColor _color;

    public ConsoleLogger() {
        _color = Console.ForegroundColor;
    }

    public void Log(string message) {
        Console.WriteLine(message);
    }

    public void LogWarn(string message) {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"\tWARNING: {message}");
        Console.ForegroundColor = _color;
    }

    public void LogError(string message) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = _color;
    }

    public void LogDebug(string message) {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = _color;
    }
}