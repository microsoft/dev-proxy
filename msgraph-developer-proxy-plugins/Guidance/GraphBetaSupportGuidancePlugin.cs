// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins.Guidance;

public class GraphBetaSupportGuidancePlugin : BaseProxyPlugin {
    public override string Name => nameof(GraphBetaSupportGuidancePlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<Regex> urlsToWatch,
                            IConfigurationSection? configSection = null) {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterResponse += AfterResponse;
    }

    private void AfterResponse(object? sender, ProxyResponseArgs e) {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null &&
            e.HasRequestUrlMatch(_urlsToWatch) &&
            ProxyUtils.IsGraphBetaRequest(request))
            _logger?.LogRequest(BuildBetaSupportMessage(request), MessageType.Warning, new LoggingContext(e.Session));
    }

    private static string GetBetaSupportGuidanceUrl() => "https://aka.ms/graph/proxy/guidance/beta-support";
    private static string[] BuildBetaSupportMessage(Request r) => new[] { $"Don't use beta APIs in production because they can change or be deprecated.", $"More info at {GetBetaSupportGuidanceUrl()}" };
}
