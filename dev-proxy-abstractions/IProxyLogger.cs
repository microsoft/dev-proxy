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

/// <summary>
/// Custom interface for logging, extending <see cref="Microsoft.Extensions.Logging.ILogger"/>
/// </summary>
/// <remarks>
/// Please use structured logging as much as possible.
/// Log Debug information with <see cref="Microsoft.Extensions.Logging.ILogger.LogDebug(string, object[])"/>
/// Log Information with <see cref="Microsoft.Extensions.Logging.ILogger.LogInformation(string, object[])"/>
/// Log Warnings with <see cref="Microsoft.Extensions.Logging.ILogger.LogWarning(string, object[])"/>
/// Log Errors with <see cref="Microsoft.Extensions.Logging.ILogger.LogError(Exception, string, object[])"/> or <see cref="Microsoft.Extensions.Logging.ILogger.LogError(string, object[])"/>
/// </remarks>
public interface IProxyLogger : ICloneable, MSLogging.ILogger
{
    public void SetLogLevel(LogLevel logLevel);
    //public LogLevel LogLevel { get; set; }

    public void LogRequest(string[] message, MessageType messageType, LoggingContext? context = null);
}