// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Logging;

public class RequestLogger : ILogger
{
    private readonly PluginEvents _pluginEvents;

    public RequestLogger(PluginEvents pluginEvents)
    {
        _pluginEvents = pluginEvents;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (state is RequestLog requestLog)
        {
            _pluginEvents.RaiseRequestLogged(new RequestLogArgs(requestLog));
        }
    }
}