// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class GraphClientRequestIdGuidancePlugin : BaseProxyPlugin
{
    public GraphClientRequestIdGuidancePlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override string Name => nameof(GraphClientRequestIdGuidancePlugin);

    public override void Register()
    {
        base.Register();

        PluginEvents.BeforeRequest += BeforeRequest;
    }

    private Task BeforeRequest(object? sender, ProxyRequestArgs e)
    {
        Request request = e.Session.HttpClient.Request;
        if (UrlsToWatch is not null &&
          e.HasRequestUrlMatch(UrlsToWatch) &&
          e.Session.HttpClient.Request.Method.ToUpper() != "OPTIONS" &&
          WarnNoClientRequestId(request))
        {
            Logger.LogRequest(BuildAddClientRequestIdMessage(request), MessageType.Warning, new LoggingContext(e.Session));

            if (!ProxyUtils.IsSdkRequest(request))
            {
                Logger.LogRequest(MessageUtils.BuildUseSdkMessage(request), MessageType.Tip, new LoggingContext(e.Session));
            }
        }

        return Task.CompletedTask;
    }

    private static bool WarnNoClientRequestId(Request request) =>
        ProxyUtils.IsGraphRequest(request) &&
        !request.Headers.HeaderExists("client-request-id");

    private static string GetClientRequestIdGuidanceUrl() => "https://aka.ms/devproxy/guidance/client-request-id";
    private static string[] BuildAddClientRequestIdMessage(Request r) => new[] {
    $"To help Microsoft investigate errors, to each request to Microsoft Graph",
    "add the client-request-id header with a unique GUID.",
    $"More info at {GetClientRequestIdGuidanceUrl()}" };
}
