using System.Text.Json;
using Microsoft.DevProxy.Abstractions;

namespace Microsoft.Extensions.Logging;

public static class ILoggerExtensions
{
    public static void LogRequest(this ILogger logger, string[] message, MessageType messageType, LoggingContext? context = null)
    {
        logger.Log(new RequestLog(message, messageType, context));
    }

    public static void LogRequest(this ILogger logger, string[] message, MessageType messageType, string method, string url)
    {
        logger.Log(new RequestLog(message, messageType, method, url));
    }

    public static void Log(this ILogger logger, RequestLog message)
    {
        logger.Log(LogLevel.Information, 0, message, exception: null, (m, _) => JsonSerializer.Serialize(m));
    }
}