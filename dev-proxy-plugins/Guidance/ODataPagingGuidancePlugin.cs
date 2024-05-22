// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class ODataPagingGuidancePlugin : BaseProxyPlugin
{
    public override string Name => nameof(ODataPagingGuidancePlugin);
    private IList<string> pagingUrls = new List<string>();

    public ODataPagingGuidancePlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override void Register()
    {
        base.Register();

        PluginEvents.BeforeRequest += OnBeforeRequest;
        PluginEvents.BeforeResponse += OnBeforeResponse;
    }

    private Task OnBeforeRequest(object? sender, ProxyRequestArgs e)
    {
        if (UrlsToWatch is null ||
            e.Session.HttpClient.Request.Method != "GET" ||
            !e.HasRequestUrlMatch(UrlsToWatch))
        {
            return Task.CompletedTask;
        }

        if (IsODataPagingUrl(e.Session.HttpClient.Request.RequestUri) &&
            !pagingUrls.Contains(e.Session.HttpClient.Request.Url))
        {
            Logger.LogRequest(BuildIncorrectPagingUrlMessage(), MessageType.Warning, new LoggingContext(e.Session));
        }

        return Task.CompletedTask;
    }

    private async Task OnBeforeResponse(object? sender, ProxyResponseArgs e)
    {
        if (UrlsToWatch is null ||
            !e.HasRequestUrlMatch(UrlsToWatch) ||
            e.Session.HttpClient.Request.Method != "GET" ||
            e.Session.HttpClient.Response.StatusCode >= 300 ||
            e.Session.HttpClient.Response.ContentType is null ||
            (!e.Session.HttpClient.Response.ContentType.Contains("json") &&
            !e.Session.HttpClient.Response.ContentType.Contains("application/atom+xml")) ||
            !e.Session.HttpClient.Response.HasBody)
        {
            return;
        }

        e.Session.HttpClient.Response.KeepBody = true;

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
            var response = JsonSerializer.Deserialize<JsonElement>(responseBody, ProxyUtils.JsonSerializerOptions);
            if (response.TryGetProperty("@odata.nextLink", out var nextLinkProperty))
            {
                nextLink = nextLinkProperty.GetString() ?? string.Empty;
            }
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "An error has occurred while parsing the response body");
        }

        return nextLink;
    }

    private string GetNextLinkFromXml(string responseBody)
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
            Logger.LogError(e.Message);
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
    "For more information about paging see https://aka.ms/devproxy/guidance/paging"
  };
}
