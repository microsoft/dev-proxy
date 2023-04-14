// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins.Guidance;

public class GraphClientRequestIdGuidancePlugin : BaseProxyPlugin
{
  public override string Name => nameof(GraphClientRequestIdGuidancePlugin);

  public override void Register(IPluginEvents pluginEvents,
                          IProxyContext context,
                          ISet<UrlToWatch> urlsToWatch,
                          IConfigurationSection? configSection = null)
  {
    base.Register(pluginEvents, context, urlsToWatch, configSection);

    pluginEvents.BeforeRequest += BeforeRequest;
  }

  private async Task BeforeRequest(object? sender, ProxyRequestArgs e)
  {
    Request request = e.Session.HttpClient.Request;
    if (_urlsToWatch is not null && e.HasRequestUrlMatch(_urlsToWatch) && WarnNoClientRequestId(request))
    {
      _logger?.LogRequest(BuildAddClientRequestIdMessage(request), MessageType.Warning, new LoggingContext(e.Session));

      if (!ProxyUtils.IsSdkRequest(request))
      {
        _logger?.LogRequest(MessageUtils.BuildUseSdkMessage(request), MessageType.Tip, new LoggingContext(e.Session));
      }
    }
  }

  private static bool WarnNoClientRequestId(Request request) =>
      ProxyUtils.IsGraphRequest(request) &&
      !request.Headers.HeaderExists("client-request-id");

  private static string GetClientRequestIdGuidanceUrl() => "https://aka.ms/graph/proxy/guidance/client-request-id";
  private static string[] BuildAddClientRequestIdMessage(Request r) => new[] {
    $"To help Microsoft investigate errors, to each request to Microsoft Graph",
    "add the client-request-id header with a unique GUID.",
    $"More info at {GetClientRequestIdGuidanceUrl()}" };
}
