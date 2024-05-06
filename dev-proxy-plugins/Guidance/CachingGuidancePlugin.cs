// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

    public CachingGuidancePlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override void Register()
    {
        base.Register();
        
        ConfigSection?.Bind(_configuration);
        PluginEvents.BeforeRequest += BeforeRequest;
    }

    private Task BeforeRequest(object? sender, ProxyRequestArgs e)
    {
        if (UrlsToWatch is null ||
          !e.HasRequestUrlMatch(UrlsToWatch) ||
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
            Logger.LogRequest(BuildCacheWarningMessage(request, _configuration.CacheThresholdSeconds, lastIntercepted), MessageType.Warning, new LoggingContext(e.Session));
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
