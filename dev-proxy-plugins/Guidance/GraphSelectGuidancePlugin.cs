// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class GraphSelectGuidancePlugin : BaseProxyPlugin
{
    public GraphSelectGuidancePlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override string Name => nameof(GraphSelectGuidancePlugin);

    public override void Register()
    {
        base.Register();

        PluginEvents.AfterResponse += AfterResponse;

        // let's not await so that it doesn't block the proxy startup
        _ = MSGraphDbUtils.GenerateMSGraphDb(Logger, true);
    }

    private Task AfterResponse(object? sender, ProxyResponseArgs e)
    {
        Request request = e.Session.HttpClient.Request;
        if (UrlsToWatch is not null &&
            e.HasRequestUrlMatch(UrlsToWatch) &&
            e.Session.HttpClient.Request.Method.ToUpper() != "OPTIONS" &&
            WarnNoSelect(request))
            Logger.LogRequest(BuildUseSelectMessage(request), MessageType.Warning, new LoggingContext(e.Session));

        return Task.CompletedTask;
    }

    private bool WarnNoSelect(Request request)
    {
        if (!ProxyUtils.IsGraphRequest(request) ||
            request.Method != "GET")
        {
            return false;
        }

        var graphVersion = ProxyUtils.GetGraphVersion(request.RequestUri.AbsoluteUri);
        var tokenizedUrl = GetTokenizedUrl(request.RequestUri.AbsoluteUri);

        if (EndpointSupportsSelect(graphVersion, tokenizedUrl))
        {
            return !request.Url.Contains("$select", StringComparison.OrdinalIgnoreCase) &&
            !request.Url.Contains("%24select", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            return false;
        }
    }

    private bool EndpointSupportsSelect(string graphVersion, string relativeUrl)
    {
        var fallback = relativeUrl.Contains("$value", StringComparison.OrdinalIgnoreCase);

        try
        {
            var dbConnection = MSGraphDbUtils.MSGraphDbConnection;
            // lookup information from the database
            var selectEndpoint = dbConnection.CreateCommand();
            selectEndpoint.CommandText = "SELECT hasSelect FROM endpoints WHERE path = @path AND graphVersion = @graphVersion";
            selectEndpoint.Parameters.AddWithValue("@path", relativeUrl);
            selectEndpoint.Parameters.AddWithValue("@graphVersion", graphVersion);
            var result = selectEndpoint.ExecuteScalar();
            var hasSelect = result != null && Convert.ToInt32(result) == 1;
            return hasSelect;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error looking up endpoint in database");
            return fallback;
        }
    }

    private static string GetSelectParameterGuidanceUrl() => "https://aka.ms/devproxy/guidance/select";
    private static string[] BuildUseSelectMessage(Request r) => new[] { $"To improve performance of your application, use the $select parameter.", $"More info at {GetSelectParameterGuidanceUrl()}" };

    private static string GetTokenizedUrl(string absoluteUrl)
    {
        var sanitizedUrl = ProxyUtils.SanitizeUrl(absoluteUrl);
        return "/" + String.Join("", new Uri(sanitizedUrl).Segments.Skip(2).Select(s => Uri.UnescapeDataString(s)));
    }
}
