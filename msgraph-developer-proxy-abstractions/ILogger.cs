// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Titanium.Web.Proxy.EventArguments;

namespace Microsoft.Graph.DeveloperProxy.Abstractions;

public enum MessageType {
    Normal,
    InterceptedRequest,
    PassedThrough,
    Warning,
    Tip,
    Failed,
    Chaos,
    Mocked
}

public class LoggingContext {
    public SessionEventArgs Session { get; }

    public LoggingContext(SessionEventArgs session)
    {
        Session = session;
    }
}

public interface ILogger {
    public void LogRequest(string[] message, MessageType messageType, LoggingContext? context = null);

    // Logging methods for non-traffic related messages
    public void LogInfo(string message);
    public void LogWarn(string message);
    public void LogError(string message);
    public void LogDebug(string message);
}