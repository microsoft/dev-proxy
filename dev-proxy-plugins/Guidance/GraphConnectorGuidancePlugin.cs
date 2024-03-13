// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;

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
                _logger?.LogRequest([ "No schema found in the request body." ], MessageType.Failed, new LoggingContext(e.Session));
                return Task.CompletedTask;
            }

            var schema = JsonSerializer.Deserialize<ExternalConnectionSchema>(schemaString, ProxyUtils.JsonSerializerOptions);
            if (schema is null || schema.Properties is null)
            {
                _logger?.LogRequest([ "Invalid schema found in the request body." ], MessageType.Failed, new LoggingContext(e.Session));
                return Task.CompletedTask;
            }

            bool hasTitle = false, hasIconUrl = false, hasUrl = false;
            foreach (var property in schema.Properties)
            {
                if (property.Labels is null)
                {
                    continue;
                }

                if (property.Labels.Contains("title", StringComparer.OrdinalIgnoreCase))
                {
                    hasTitle = true;
                }
                if (property.Labels.Contains("iconUrl", StringComparer.OrdinalIgnoreCase))
                {
                    hasIconUrl = true;
                }
                if (property.Labels.Contains("url", StringComparer.OrdinalIgnoreCase))
                {
                    hasUrl = true;
                }
            }

            if (!hasTitle || !hasIconUrl || !hasUrl)
            {
                string[] missingLabels = [
                    !hasTitle ? "title" : "",
                    !hasIconUrl ? "iconUrl" : "",
                    !hasUrl ? "url" : ""
                ];

                _logger?.LogRequest(
                    [
                        $"The schema is missing the following semantic labels: {string.Join(", ", missingLabels.Where(s => s != ""))}.",
                        "Ingested content might not show up in Microsoft Copilot for Microsoft 365.",
                        "More information: https://aka.ms/devproxy/guidance/gc/ux"
                    ],
                    MessageType.Failed, new LoggingContext(e.Session)
                );
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "An error has occurred while deserializing the request body");
        }

        return Task.CompletedTask;
    }
}
