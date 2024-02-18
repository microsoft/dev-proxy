// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Runtime.Serialization;
using Titanium.Web.Proxy.EventArguments;
using MSLogging = Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Abstractions;

public enum MessageType
{
    Normal,
    InterceptedRequest,
    PassedThrough,
    Warning,
    Tip,
    Failed,
    Chaos,
    Mocked,
    InterceptedResponse
}

public class LoggingContext
{
    public SessionEventArgs Session { get; }

    public LoggingContext(SessionEventArgs session)
    {
        Session = session;
    }
}

public enum LogLevel
{
    [EnumMember(Value = "debug")]
    Debug,
    [EnumMember(Value = "info")]
    Info,
    [EnumMember(Value = "warn")]
    Warn,
    [EnumMember(Value = "error")]
    Error
}

public interface IProxyLogger : ICloneable, MSLogging.ILogger
{
    public void SetLogLevel(LogLevel logLevel);
    public void LogRequest(string[] message, MessageType messageType, LoggingContext? context = null);
}