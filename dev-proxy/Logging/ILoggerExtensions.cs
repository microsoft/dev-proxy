// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License

namespace Microsoft.Extensions.Logging;

public static class ILoggerExtensions
{
    public static IDisposable? BeginScope(this ILogger logger, string method, string url, int requestId) =>
      logger.BeginScope(new Dictionary<string, object>
      {
          { nameof(method), method },
          { nameof(url), url },
          { nameof(requestId), requestId }
      });
}