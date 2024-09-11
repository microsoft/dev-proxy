// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License

#pragma warning disable IDE0130
namespace Microsoft.Extensions.Logging;
#pragma warning restore IDE0130

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