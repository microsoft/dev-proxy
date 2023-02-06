// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins.Guidance;

public class GraphSelectGuidancePlugin : BaseProxyPlugin {
    public override string Name => nameof(GraphSelectGuidancePlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<Regex> urlsToWatch,
                            IConfigurationSection? configSection = null) {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterResponse += AfterResponse;
    }

    private void AfterResponse(object? sender, ProxyResponseArgs e) {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null && e.HasRequestUrlMatch(_urlsToWatch) && WarnNoSelect(request))
            _logger?.LogRequest(BuildUseSelectMessage(request), MessageType.Warning, new LoggingContext(e.Session));
    }

    private static bool WarnNoSelect(Request request) =>
        ProxyUtils.IsGraphRequest(request) &&
        request.Method == "GET" &&
        !request.Url.Contains("$select", StringComparison.OrdinalIgnoreCase) &&
        !request.Url.Contains("%24select", StringComparison.OrdinalIgnoreCase);

    private static string GetSelectParameterGuidanceUrl() => "https://learn.microsoft.com/graph/query-parameters#select-parameter";
    private static string[] BuildUseSelectMessage(Request r) => new[] { $"To improve performance of your application, use the $select parameter.", $"More info at {GetSelectParameterGuidanceUrl()}" };
}
