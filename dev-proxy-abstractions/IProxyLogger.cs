// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Titanium.Web.Proxy.EventArguments;
using Microsoft.Extensions.Logging;

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
    InterceptedResponse,
    FinishedProcessingRequest
}

public class LoggingContext
{
    public SessionEventArgs Session { get; }

    public LoggingContext(SessionEventArgs session)
    {
        Session = session;
    }
}

public interface IProxyLogger : ICloneable, ILogger
{
    public LogLevel LogLevel { get; set; }
    public void LogRequest(string[] message, MessageType messageType, LoggingContext? context = null);
    public void LogRequest(string[] message, MessageType messageType, string method, string url);
}