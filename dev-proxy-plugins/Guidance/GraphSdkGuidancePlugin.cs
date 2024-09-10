// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class GraphSdkGuidancePlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : BaseProxyPlugin(pluginEvents, context, logger, urlsToWatch, configSection)
{
    public override string Name => nameof(GraphSdkGuidancePlugin);

    public override async Task RegisterAsync()
    {
        await base.RegisterAsync();

        PluginEvents.AfterResponse += OnAfterResponseAsync;
    }

    private Task OnAfterResponseAsync(object? sender, ProxyResponseArgs e)
    {
        Request request = e.Session.HttpClient.Request;
        // only show the message if there is an error.
        if (e.Session.HttpClient.Response.StatusCode >= 400 &&
            UrlsToWatch is not null &&
            e.HasRequestUrlMatch(UrlsToWatch) &&
            !string.Equals(e.Session.HttpClient.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase) &&
            WarnNoSdk(request))
        {
            Logger.LogRequest(MessageUtils.BuildUseSdkForErrorsMessage(request), MessageType.Tip, new LoggingContext(e.Session));
        }

        return Task.CompletedTask;
    }

    private static bool WarnNoSdk(Request request) => ProxyUtils.IsGraphRequest(request) && !ProxyUtils.IsSdkRequest(request);
}
