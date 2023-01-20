// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins.Guidance;

public class GraphBetaSupportGuidancePlugin : IProxyPlugin {
    private ISet<Regex>? _urlsToWatch;
    private ILogger? _logger;
    public string Name => nameof(GraphBetaSupportGuidancePlugin);

    public void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<Regex> urlsToWatch,
                            IConfigurationSection? configSection = null) {
        if (pluginEvents is null) {
            throw new ArgumentNullException(nameof(pluginEvents));
        }

        if (context is null || context.Logger is null) {
            throw new ArgumentException($"{nameof(context)} must not be null and must supply a non-null Logger", nameof(context));
        }

        if (urlsToWatch is null || urlsToWatch.Count == 0) {
            throw new ArgumentException($"{nameof(urlsToWatch)} cannot be null or empty", nameof(urlsToWatch));
        }

        _urlsToWatch = urlsToWatch;
        _logger = context.Logger;

        pluginEvents.AfterResponse += AfterResponse;
    }

    private void AfterResponse(object? sender, ProxyResponseArgs e) {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null &&
            e.HasRequestUrlMatch(_urlsToWatch) &&
            ProxyUtils.IsGraphBetaRequest(request))
            _logger?.LogRequest(BuildBetaSupportMessage(request), MessageType.Warning, new LoggingContext(e.Session));
    }

    private static string GetBetaSupportGuidanceUrl() => "https://learn.microsoft.com/graph/versioning-and-support#beta-version";
    private static string[] BuildBetaSupportMessage(Request r) => new[] { $"Don't use beta APIs in production because they can change or be deprecated.", $"More info at {GetBetaSupportGuidanceUrl()}" };
}
