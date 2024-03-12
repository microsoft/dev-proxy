// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.Http;
using System.Text.Json;

namespace Microsoft.DevProxy.Plugins.Guidance;

class ExternalConnectionSchema
{
    public string? BaseType { get; set; }
    public ExternalConnectionSchemaProperty[]? Properties { get; set; }
}

class ExternalConnectionSchemaProperty
{
    public string[]? Aliases { get; set; }
    public bool? IsQueryable { get; set; }
    public bool? IsRefinable { get; set; }
    public bool? IsRetrievable { get; set; }
    public bool? IsSearchable { get; set; }
    public string[]? Labels { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
}

public class GraphConnectorGuidancePlugin : BaseProxyPlugin
{
    public override string Name => nameof(GraphConnectorGuidancePlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.BeforeRequest += BeforeRequest;
    }

    private Task BeforeRequest(object sender, ProxyRequestArgs e)
    {
        if (_urlsToWatch is null ||
          !e.HasRequestUrlMatch(_urlsToWatch) ||
          e.Session.HttpClient.Request.Method.ToUpper() != "PATCH")
        {
            return Task.CompletedTask;
        }

        try
        {
            var schemaString = e.Session.HttpClient.Request.BodyString;
            if (string.IsNullOrEmpty(schemaString))
            {
                return Task.CompletedTask;
            }

            var schema = JsonSerializer.Deserialize<ExternalConnectionSchema>(schemaString, ProxyUtils.JsonSerializerOptions);
        }
        catch
        {

        }

        return Task.CompletedTask;
    }

    private Task AfterResponse(object? sender, ProxyResponseArgs e)
    {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null &&
            e.HasRequestUrlMatch(_urlsToWatch) &&
            e.Session.HttpClient.Request.Method.ToUpper() != "OPTIONS" &&
            ProxyUtils.IsGraphBetaRequest(request))
            _logger?.LogRequest(BuildBetaSupportMessage(request), MessageType.Warning, new LoggingContext(e.Session));
        return Task.CompletedTask;
    }

    private static string GetBetaSupportGuidanceUrl() => "https://aka.ms/devproxy/guidance/beta-support";
    private static string[] BuildBetaSupportMessage(Request r) => new[] { $"Don't use beta APIs in production because they can change or be deprecated.", $"More info at {GetBetaSupportGuidanceUrl()}" };
}
