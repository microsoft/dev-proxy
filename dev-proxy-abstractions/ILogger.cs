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
/// <see cref="ILogger.LogDebug(string)"/> becomes <see cref="Microsoft.Extensions.Logging.ILogger.LogDebug(string, object[])"/>
/// <see cref="ILogger.LogInfo(string)"/> becomes <see cref="Microsoft.Extensions.Logging.ILogger.LogInformation(string, object[])"/>
/// <see cref="ILogger.LogWarn(string)"/> becomes <see cref="Microsoft.Extensions.Logging.ILogger.LogWarning(string, object[])"/>
/// <see cref="ILogger.LogError(string)"/> becomes <see cref="Microsoft.Extensions.Logging.ILogger.LogError(string, object[])"/> or <see cref="Microsoft.Extensions.Logging.ILogger.LogError(Exception, string, object[])"/>
/// </remarks>
public interface ILogger : ICloneable, MSLogging.ILogger
{
    public LogLevel LogLevel { get; set; }

    public void LogRequest(string[] message, MessageType messageType, LoggingContext? context = null);

    // Logging methods for non-traffic related messages
    public void LogInfo(string message);
    [Obsolete("Moved to structured logging.")]
    public void LogWarn(string message);
    public void LogError(string message);
    public void LogDebug(string message);
}