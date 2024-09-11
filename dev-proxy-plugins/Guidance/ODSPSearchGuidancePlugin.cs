// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class ODSPSearchGuidancePlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : BaseProxyPlugin(pluginEvents, context, logger, urlsToWatch, configSection)
{
    public override string Name => nameof(ODSPSearchGuidancePlugin);

    public override async Task RegisterAsync()
    {
        await base.RegisterAsync();

        PluginEvents.BeforeRequest += BeforeRequestAsync;
    }

    private Task BeforeRequestAsync(object sender, ProxyRequestArgs e)
    {
        Request request = e.Session.HttpClient.Request;
        if (UrlsToWatch is not null &&
            e.HasRequestUrlMatch(UrlsToWatch) &&
            !string.Equals(e.Session.HttpClient.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase) &&
            WarnDeprecatedSearch(request))
            Logger.LogRequest(BuildUseGraphSearchMessage(), MessageType.Warning, new LoggingContext(e.Session));

        return Task.CompletedTask;
    }

    private static bool WarnDeprecatedSearch(Request request)
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

    private static string[] BuildUseGraphSearchMessage() => [$"To get the best search experience, use the Microsoft Search APIs in Microsoft Graph.", $"More info at https://aka.ms/devproxy/guidance/odspsearch"];
}
