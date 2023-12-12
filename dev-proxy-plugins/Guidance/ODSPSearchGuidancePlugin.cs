// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class ODSPSearchGuidancePlugin : BaseProxyPlugin
{
    public override string Name => nameof(ODSPSearchGuidancePlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.BeforeRequest += BeforeRequest;
    }

    private Task BeforeRequest(object sender, ProxyRequestArgs e)
    {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null &&
            e.HasRequestUrlMatch(_urlsToWatch) &&
            e.Session.HttpClient.Request.Method.ToUpper() != "OPTIONS" &&
            WarnDeprecatedSearch(request))
            _logger?.LogRequest(BuildUseGraphSearchMessage(), MessageType.Warning, new LoggingContext(e.Session));
            
        return Task.CompletedTask;
    }

    private bool WarnDeprecatedSearch(Request request)
    {
        if (!ProxyUtils.IsGraphRequest(request) ||
            request.Method != "GET")
        {
            return false;
        }

        // graph.microsoft.com/{version}/drives/{drive-id}/root/search(q='{search-text}')
        // graph.microsoft.com/{version}/groups/{group-id}/drive/root/search(q='{search-text}')
        // graph.microsoft.com/{version}/me/drive/root/search(q='{search-text}')
        // graph.microsoft.com/{version}/sites/{site-id}/drive/root/search(q='{search-text}')
        // graph.microsoft.com/{version}/users/{user-id}/drive/root/search(q='{search-text}')
        // graph.microsoft.com/{version}/sites?search={query}
        if (request.RequestUri.AbsolutePath.Contains("/search(q=", StringComparison.OrdinalIgnoreCase) ||
            (request.RequestUri.AbsolutePath.EndsWith("/sites", StringComparison.OrdinalIgnoreCase) &&
            request.RequestUri.Query.Contains("search=", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private static string[] BuildUseGraphSearchMessage() => new[] { $"To get the best search experience, use the Microsoft Search APIs in Microsoft Graph.", $"More info at https://aka.ms/devproxy/guidance/odspsearch" };
}
