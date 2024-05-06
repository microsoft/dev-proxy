// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Logging;

public class RequestLoggerProvider : ILoggerProvider
{
    private readonly PluginEvents _pluginEvents;
    private readonly ConcurrentDictionary<string, RequestLogger> _loggers = new();

    public RequestLoggerProvider(PluginEvents pluginEvents)
    {
        _pluginEvents = pluginEvents;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new RequestLogger(_pluginEvents));


    public void Dispose() => _loggers.Clear();
}