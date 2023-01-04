// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins.Guidance;

public class SelectGuidancePlugin : IProxyPlugin {
    private ISet<Regex>? _urlsToWatch;
    private ILogger? _logger;
    public string Name => nameof(SelectGuidancePlugin);

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

        pluginEvents.Request += OnRequest;
    }

    private void OnRequest(object? sender, ProxyRequestArgs e) {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null && e.ShouldExecute(_urlsToWatch) && WarnNoSelect(request))
            _logger?.LogWarn(BuildUseSelectMessage(request));
    }

    private static bool WarnNoSelect(Request request) =>
        ProxyUtils.IsGraphRequest(request) &&
            request.Method == "GET" &&
            !request.Url.Contains("$select", StringComparison.OrdinalIgnoreCase);

    private static string GetSelectParameterGuidanceUrl() => "https://learn.microsoft.com/graph/query-parameters#select-parameter";
    private static string BuildUseSelectMessage(Request r) => $"To improve performance of your application, use the $select parameter when calling {r.RequestUriString}. More info at {GetSelectParameterGuidanceUrl()}";
}
