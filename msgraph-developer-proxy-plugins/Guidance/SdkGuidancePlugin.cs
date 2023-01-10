// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins.Guidance;

public class GraphSdkGuidancePlugin : IProxyPlugin {
    private ISet<Regex>? _urlsToWatch;
    private ILogger? _logger;
    public string Name => nameof(GraphSdkGuidancePlugin);

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

        pluginEvents.AfterResponse += OnAfterResponse;
    }

    private void OnAfterResponse(object? sender, ProxyResponseArgs e) {
        Request request = e.Session.HttpClient.Request;
        // only show the message if there is an error.
        if (e.Session.HttpClient.Response.StatusCode >= 400 
            && _urlsToWatch is not null 
            && e.HasRequestUrlMatch(_urlsToWatch) 
            && WarnNoSdk(request)) {
            _logger?.LogWarn(MessageUtils.BuildUseSdkMessage(request));
        }
    }

    private static bool WarnNoSdk(Request request) => ProxyUtils.IsGraphRequest(request) && !ProxyUtils.IsSdkRequest(request);
}
