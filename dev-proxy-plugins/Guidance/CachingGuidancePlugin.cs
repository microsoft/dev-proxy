// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class CachingGuidancePluginConfiguration
{
  public int CacheThresholdSeconds { get; set; } = 5;
}

public class CachingGuidancePlugin : BaseProxyPlugin
{
  public override string Name => nameof(CachingGuidancePlugin);
  private readonly CachingGuidancePluginConfiguration _configuration = new();
  private IDictionary<string, DateTime> _interceptedRequests = new Dictionary<string, DateTime>();

  public override void Register(IPluginEvents pluginEvents,
                          IProxyContext context,
                          ISet<UrlToWatch> urlsToWatch,
                          IConfigurationSection? configSection = null)
  {
    base.Register(pluginEvents, context, urlsToWatch, configSection);
    configSection?.Bind(_configuration);

    pluginEvents.BeforeRequest += BeforeRequest;
  }

  private Task BeforeRequest(object? sender, ProxyRequestArgs e)
  {
    if (_urlsToWatch is null ||
      !e.HasRequestUrlMatch(_urlsToWatch) ||
      e.Session.HttpClient.Request.Method.ToUpper() == "OPTIONS")
    {
      return Task.CompletedTask;
    }

    Request request = e.Session.HttpClient.Request;
    var url = request.RequestUri.AbsoluteUri;
    var now = DateTime.Now;

    if (!_interceptedRequests.ContainsKey(url))
    {
      _interceptedRequests.Add(url, now);
      return Task.CompletedTask;
    }

    var lastIntercepted = _interceptedRequests[url];
    var secondsSinceLastIntercepted = (now - lastIntercepted).TotalSeconds;
    if (secondsSinceLastIntercepted <= _configuration.CacheThresholdSeconds)
    {
      _logger?.LogRequest(BuildCacheWarningMessage(request, _configuration.CacheThresholdSeconds, lastIntercepted), MessageType.Warning, new LoggingContext(e.Session));
    }

    _interceptedRequests[url] = now;
    return Task.CompletedTask;
  }

  private static string[] BuildCacheWarningMessage(Request r, int _warningSeconds, DateTime lastIntercepted) => new[] {
    $"Another request to {r.RequestUri.PathAndQuery} intercepted within {_warningSeconds} seconds.",
    $"Last intercepted at {lastIntercepted}.",
    "Consider using cache to avoid calling the API too often."
  };
}
