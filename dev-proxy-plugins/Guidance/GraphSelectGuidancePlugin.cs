// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Guidance;

public class GraphSelectGuidancePlugin : BaseProxyPlugin
{
    public override string Name => nameof(GraphSelectGuidancePlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterResponse += AfterResponse;

        // for background db refresh, let's use a separate logger
        // that only logs warnings and errors
        var _logger2 = (IProxyLogger)context.Logger.Clone();
        _logger2.LogLevel = LogLevel.Warning;
        // let's not await so that it doesn't block the proxy startup
        _ = MSGraphDbUtils.GenerateMSGraphDb(_logger2, true);
    }

    private Task AfterResponse(object? sender, ProxyResponseArgs e)
    {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null &&
            e.HasRequestUrlMatch(_urlsToWatch) &&
            e.Session.HttpClient.Request.Method.ToUpper() != "OPTIONS" &&
            WarnNoSelect(request))
            _logger?.LogRequest(BuildUseSelectMessage(request), MessageType.Warning, new LoggingContext(e.Session));

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
            _logger?.LogError(ex, "Error looking up endpoint in database");
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
