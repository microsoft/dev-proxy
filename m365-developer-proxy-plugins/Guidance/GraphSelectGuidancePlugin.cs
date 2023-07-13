// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft365.DeveloperProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft365.DeveloperProxy.Plugins.Guidance;

public class GraphSelectGuidancePlugin : BaseProxyPlugin
{
    public override string Name => nameof(GraphSelectGuidancePlugin);
    private readonly Dictionary<string, OpenApiDocument> _openApiDocuments = new();

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        var proxyFolder = Path.GetDirectoryName(context.Configuration.ConfigFile);
        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        LoadOpenAPIFiles(proxyFolder!);
        stopwatch.Stop();
        UpdateOpenAPIGraphFilesIfNecessary(proxyFolder!);

        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterResponse += AfterResponse;
    }

    private async Task UpdateOpenAPIGraphFilesIfNecessary(string proxyFolder)
    {
        var modified = false;
        var versions = new[] { "v1.0", "beta" };
        foreach (var version in versions)
        {
            try
            {
                var file = new FileInfo(Path.Combine(proxyFolder, "GraphProxyPlugins", $"graph-{version.Replace(".", "_")}-openapi.yaml"));
                if (file.LastWriteTime.Date == DateTime.Now.Date)
                {
                    // file already updated today
                    continue;
                }

                var url = $"https://raw.githubusercontent.com/microsoftgraph/msgraph-metadata/master/openapi/{version}/openapi.yaml";
                var client = new HttpClient();
                var response = await client.GetStringAsync(url);
                File.WriteAllText(file.FullName, response);
                modified = true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex.Message);
            }
        }

        if (modified)
        {
            LoadOpenAPIFiles(proxyFolder);
        }
    }

    private async void LoadOpenAPIFiles(string proxyFolder)
    {
        var versions = new[] { "v1.0", "beta" };
        foreach (var version in versions)
        {
            var file = new FileInfo(Path.Combine(proxyFolder, "GraphProxyPlugins", $"graph-{version.Replace(".", "_")}-openapi.yaml"));
            if (!file.Exists)
            {
                continue;
            }

            var openApiDocument = await new OpenApiStreamReader().ReadAsync(file.OpenRead());
            _openApiDocuments[version] = openApiDocument.OpenApiDocument;
        }
    }

    private async Task AfterResponse(object? sender, ProxyResponseArgs e)
    {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null && e.HasRequestUrlMatch(_urlsToWatch) && WarnNoSelect(request))
            _logger?.LogRequest(BuildUseSelectMessage(request), MessageType.Warning, new LoggingContext(e.Session));
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

        if (!_openApiDocuments.ContainsKey(graphVersion))
        {
            return fallback;
        }

        var relativeUrlPattern = Regex.Replace(relativeUrl, @"{[^}]+}", @"{[a-zA-Z-]+}");
        var relativeUrlRegex = new Regex($"^{relativeUrlPattern}$");

        var openApiDocument = _openApiDocuments[graphVersion];
        var pathString = openApiDocument.Paths.Keys.FirstOrDefault(k => relativeUrlRegex.IsMatch(k));
        if (pathString is null ||
            !openApiDocument.Paths[pathString].Operations.ContainsKey(OperationType.Get))
        {
            return fallback;
        }

        var operation = openApiDocument.Paths[pathString].Operations[OperationType.Get];
        var parameters = operation.Parameters;
        return parameters.Any(p => p.Name == "$select");
    }

    private static string GetSelectParameterGuidanceUrl() => "https://aka.ms/m365/proxy/guidance/select";
    private static string[] BuildUseSelectMessage(Request r) => new[] { $"To improve performance of your application, use the $select parameter.", $"More info at {GetSelectParameterGuidanceUrl()}" };

    private static string GetTokenizedUrl(string absoluteUrl)
    {
        var sanitizedUrl = ProxyUtils.SanitizeUrl(absoluteUrl);
        return "/" + String.Join("", new Uri(sanitizedUrl).Segments.Skip(2).Select(s => Uri.UnescapeDataString(s)));
    }
}
