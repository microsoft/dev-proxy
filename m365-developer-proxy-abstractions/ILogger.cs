// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Runtime.Serialization;
using Titanium.Web.Proxy.EventArguments;

namespace Microsoft365.DeveloperProxy.Abstractions;

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

public enum LogLevel {
    [EnumMember(Value = "debug")]
    Debug,
    [EnumMember(Value = "info")]
    Info,
    [EnumMember(Value = "warn")]
    Warn,
    [EnumMember(Value = "error")]
    Error
}

public interface ILogger: ICloneable {
    public LogLevel LogLevel { get; set; }

    public void LogRequest(string[] message, MessageType messageType, LoggingContext? context = null);

    // Logging methods for non-traffic related messages
    public void LogInfo(string message);
    public void LogWarn(string message);
    public void LogError(string message);
    public void LogDebug(string message);
}