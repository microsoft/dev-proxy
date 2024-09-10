// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Microsoft.VisualStudio.Threading;

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
            var joinableTaskContext = new JoinableTaskContext();
            var joinableTaskFactory = new JoinableTaskFactory(joinableTaskContext);
            
            joinableTaskFactory.Run(async () => await _pluginEvents.RaiseRequestLoggedAsync(new RequestLogArgs(requestLog)));
        }
    }
}