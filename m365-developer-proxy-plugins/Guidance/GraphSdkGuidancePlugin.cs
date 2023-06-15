// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft365.DeveloperProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft365.DeveloperProxy.Plugins.Guidance;

public class GraphSdkGuidancePlugin : BaseProxyPlugin {
    public override string Name => nameof(GraphSdkGuidancePlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null) {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterResponse += OnAfterResponse;
    }

    private async Task OnAfterResponse(object? sender, ProxyResponseArgs e) {
        Request request = e.Session.HttpClient.Request;
        // only show the message if there is an error.
        if (e.Session.HttpClient.Response.StatusCode >= 400 
            && _urlsToWatch is not null 
            && e.HasRequestUrlMatch(_urlsToWatch) 
            && WarnNoSdk(request)) {
            _logger?.LogRequest(MessageUtils.BuildUseSdkForErrorsMessage(request), MessageType.Tip, new LoggingContext(e.Session));
        }
    }

    private static bool WarnNoSdk(Request request) => ProxyUtils.IsGraphRequest(request) && !ProxyUtils.IsSdkRequest(request);
}
