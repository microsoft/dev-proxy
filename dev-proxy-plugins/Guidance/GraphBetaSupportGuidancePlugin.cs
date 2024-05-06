// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class GraphBetaSupportGuidancePlugin : BaseProxyPlugin
{
    public GraphBetaSupportGuidancePlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override string Name => nameof(GraphBetaSupportGuidancePlugin);

    public override void Register()
    {
        base.Register();

        PluginEvents.AfterResponse += AfterResponse;
    }

    private Task AfterResponse(object? sender, ProxyResponseArgs e)
    {
        Request request = e.Session.HttpClient.Request;
        if (UrlsToWatch is not null &&
            e.HasRequestUrlMatch(UrlsToWatch) &&
            e.Session.HttpClient.Request.Method.ToUpper() != "OPTIONS" &&
            ProxyUtils.IsGraphBetaRequest(request))
            Logger.LogRequest(BuildBetaSupportMessage(request), MessageType.Warning, new LoggingContext(e.Session));
        return Task.CompletedTask;
    }

    private static string GetBetaSupportGuidanceUrl() => "https://aka.ms/devproxy/guidance/beta-support";
    private static string[] BuildBetaSupportMessage(Request r) => [$"Don't use beta APIs in production because they can change or be deprecated.", $"More info at {GetBetaSupportGuidanceUrl()}"];
}
