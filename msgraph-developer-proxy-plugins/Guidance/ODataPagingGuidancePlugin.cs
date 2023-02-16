// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Microsoft.Graph.DeveloperProxy.Plugins.Guidance;

public class ODataPagingGuidancePlugin : BaseProxyPlugin
{
  public override string Name => nameof(ODataPagingGuidancePlugin);
  private IList<string> pagingUrls = new List<string>();

  public override void Register(IPluginEvents pluginEvents,
                          IProxyContext context,
                          ISet<Regex> urlsToWatch,
                          IConfigurationSection? configSection = null)
  {
    base.Register(pluginEvents, context, urlsToWatch, configSection);

    pluginEvents.BeforeRequest += OnBeforeRequest;
    pluginEvents.BeforeResponse += OnBeforeResponse;
    pluginEvents.AfterResponse += OnAfterResponse;
  }

  private void OnBeforeRequest(object? sender, ProxyRequestArgs e)
  {
    if (_urlsToWatch is null ||
        !e.HasRequestUrlMatch(_urlsToWatch))
    {
      return;
    }

    if (IsODataPagingUrl(e.Session.HttpClient.Request.RequestUri) &&
        !pagingUrls.Contains(e.Session.HttpClient.Request.Url))
    {
      _logger?.LogRequest(BuildIncorrectPagingUrlMessage(), MessageType.Warning, new LoggingContext(e.Session));
    }
  }

  private async void OnBeforeResponse(object? sender, ProxyResponseArgs e)
  {
    if (_urlsToWatch is null ||
        !e.HasRequestUrlMatch(_urlsToWatch))
    {
      return;
    }

    // necessary for the response body to be available in the AfterResponse event
    await e.Session.GetResponseBodyAsString();
  }

  private async void OnAfterResponse(object? sender, ProxyResponseArgs e)
  {
    if (_urlsToWatch is null ||
        !e.HasRequestUrlMatch(_urlsToWatch) ||
        e.Session.HttpClient.Response.StatusCode >= 300 ||
        e.Session.HttpClient.Response.ContentType is null)
    {
      return;
    }

    var nextLink = string.Empty;
    var bodyString = await e.Session.GetResponseBodyAsString();
    if (string.IsNullOrEmpty(bodyString))
    {
      return;
    }

    var contentType = e.Session.HttpClient.Response.ContentType;
    if (contentType.Contains("json"))
    {
      nextLink = GetNextLinkFromJson(bodyString);
    }
    else if (contentType.Contains("application/atom+xml"))
    {
      nextLink = GetNextLinkFromXml(bodyString);
    }

    if (!String.IsNullOrEmpty(nextLink))
    {
      pagingUrls.Add(nextLink);
    }
  }

  private string GetNextLinkFromJson(string responseBody)
  {
    var nextLink = string.Empty;

    try
    {
      var response = JsonSerializer.Deserialize<JsonElement>(responseBody);
      JsonElement nextLinkProperty = new JsonElement();
      if (response.TryGetProperty("@odata.nextLink", out nextLinkProperty)) {
        nextLink = nextLinkProperty.GetString() ?? string.Empty;
      }
    }
    catch (Exception e)
    {
      _logger?.LogDebug($"An error has occurred while parsing the response body: {e.Message}. {e.StackTrace}");
    }

    return nextLink;
  }

  private static string GetNextLinkFromXml(string responseBody)
  {
    var nextLink = string.Empty;

    try
    {
      var doc = XDocument.Parse(responseBody);
      nextLink = doc
        .Descendants()
        .Where(e => e.Name.LocalName == "link" && e.Attribute("rel")?.Value == "next")
        .FirstOrDefault()
        ?.Attribute("href")?.Value ?? string.Empty;
    }
    catch (Exception e)
    {
      Console.WriteLine(e.Message);
    }

    return nextLink;
  }

  private static bool IsODataPagingUrl(Uri uri) =>
    uri.Query.Contains("$skip") ||
    uri.Query.Contains("%24skip") ||
    uri.Query.Contains("$skiptoken") ||
    uri.Query.Contains("%24skiptoken");

  private static string[] BuildIncorrectPagingUrlMessage() => new[] {
    "This paging URL seems to be created manually and is not aligned with paging information from the API.",
    "This could lead to incorrect data in your app.",
    "For more information about paging see https://aka.ms/graph/proxy/guidance/paging"
  };
}
