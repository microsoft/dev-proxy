// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class GraphBetaSupportGuidancePlugin : BaseProxyPlugin {
    public override string Name => nameof(GraphBetaSupportGuidancePlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null) {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterResponse += AfterResponse;
    }

    private async Task AfterResponse(object? sender, ProxyResponseArgs e) {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null &&
            e.HasRequestUrlMatch(_urlsToWatch) &&
            ProxyUtils.IsGraphBetaRequest(request))
            _logger?.LogRequest(BuildBetaSupportMessage(request), MessageType.Warning, new LoggingContext(e.Session));
    }

    private static string GetBetaSupportGuidanceUrl() => "https://aka.ms/devproxy/guidance/beta-support";
    private static string[] BuildBetaSupportMessage(Request r) => new[] { $"Don't use beta APIs in production because they can change or be deprecated.", $"More info at {GetBetaSupportGuidanceUrl()}" };
}
