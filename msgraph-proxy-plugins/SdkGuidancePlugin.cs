// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins {
    public class SdkGuidancePlugin : IProxyPlugin {
        private ISet<Regex>? _urlsToWatch;
        private ILogger? _logger;
        public string Name => nameof(SdkGuidancePlugin);

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
            if (_urlsToWatch is not null && e.ShouldExecute(_urlsToWatch) && WarnNoSdk(request))
                _logger?.LogWarn(BuildUseSdkMessage(request));
        }

        private static bool WarnNoSdk(Request request) => ProxyUtils.IsGraphRequest(request) && !ProxyUtils.IsSdkRequest(request);

        private static string BuildUseSdkMessage(Request r) => $"To handle API errors more easily, use the Graph SDK. More info at {GetMoveToSdkUrl(r)}";

        private static string GetMoveToSdkUrl(Request request) {
            // TODO: return language-specific guidance links based on the language detected from the User-Agent
            return "https://aka.ms/move-to-graph-js-sdk";
        }
    }
}
