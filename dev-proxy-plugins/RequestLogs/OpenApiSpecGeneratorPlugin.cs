// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy.EventArguments;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Extensions;
using System.Text.Json;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Writers;
using Microsoft.OpenApi;
using Titanium.Web.Proxy.Http;
using System.Web;
using System.Collections.Specialized;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.RequestLogs;

class GeneratedByOpenApiExtension : IOpenApiExtension
{
    public void Write(IOpenApiWriter writer, OpenApiSpecVersion specVersion)
    {
        writer.WriteStartObject();
        writer.WriteProperty("toolName", "Dev Proxy");
        writer.WriteProperty("toolVersion", ProxyUtils.ProductVersion);
        writer.WriteEndObject();
    }
}

public class OpenApiSpecGeneratorPlugin : BaseProxyPlugin
{
    // from: https://github.com/jonluca/har-to-openapi/blob/0d44409162c0a127cdaccd60b0a270ecd361b829/src/utils/headers.ts
    private static readonly string[] standardHeaders =
    [
        ":authority",
        ":method",
        ":path",
        ":scheme",
        ":status",
        "a-im",
        "accept",
        "accept-additions",
        "accept-ch",
        "accept-ch-lifetime",
        "accept-charset",
        "accept-datetime",
        "accept-encoding",
        "accept-features",
        "accept-language",
        "accept-patch",
        "accept-post",
        "accept-ranges",
        "access-control-allow-credentials",
        "access-control-allow-headers",
        "access-control-allow-methods",
        "access-control-allow-origin",
        "access-control-expose-headers",
        "access-control-max-age",
        "access-control-request-headers",
        "access-control-request-method",
        "age",
        "allow",
        "alpn",
        "alt-svc",
        "alternate-protocol",
        "alternates",
        "amp-access-control-allow-source-origin",
        "apply-to-redirect-ref",
        "authentication-info",
        "authorization",
        "c-ext",
        "c-man",
        "c-opt",
        "c-pep",
        "c-pep-info",
        "cache-control",
        "ch",
        "connection",
        "content-base",
        "content-disposition",
        "content-dpr",
        "content-encoding",
        "content-id",
        "content-language",
        "content-length",
        "content-location",
        "content-md5",
        "content-range",
        "content-script-type",
        "content-security-policy",
        "content-security-policy-report-only",
        "content-style-type",
        "content-type",
        "content-version",
        "cookie",
        "cookie2",
        "cross-origin-resource-policy",
        "dasl",
        "date",
        "dav",
        "default-style",
        "delta-base",
        "depth",
        "derived-from",
        "destination",
        "differential-id",
        "digest",
        "dnt",
        "dpr",
        "encryption",
        "encryption-key",
        "etag",
        "expect",
        "expect-ct",
        "expires",
        "ext",
        "forwarded",
        "from",
        "front-end-https",
        "getprofile",
        "host",
        "http2-settings",
        "if",
        "if-match",
        "if-modified-since",
        "if-none-match",
        "if-range",
        "if-schedule-tag-match",
        "if-unmodified-since",
        "im",
        "keep-alive",
        "key",
        "label",
        "last-event-id",
        "last-modified",
        "link",
        "link-template",
        "location",
        "lock-token",
        "man",
        "max-forwards",
        "md",
        "meter",
        "mime-version",
        "negotiate",
        "nice",
        "opt",
        "ordering-type",
        "origin",
        "origin-trial",
        "overwrite",
        "p3p",
        "pep",
        "pep-info",
        "pics-label",
        "poe",
        "poe-links",
        "position",
        "pragma",
        "prefer",
        "preference-applied",
        "profileobject",
        "protocol",
        "protocol-info",
        "protocol-query",
        "protocol-request",
        "proxy-authenticate",
        "proxy-authentication-info",
        "proxy-authorization",
        "proxy-connection",
        "proxy-features",
        "proxy-instruction",
        "public",
        "range",
        "redirect-ref",
        "referer",
        "referrer-policy",
        "report-to",
        "retry-after",
        "rw",
        "safe",
        "save-data",
        "schedule-reply",
        "schedule-tag",
        "sec-ch-ua",
        "sec-ch-ua-mobile",
        "sec-ch-ua-platform",
        "sec-fetch-dest",
        "sec-fetch-mode",
        "sec-fetch-site",
        "sec-fetch-user",
        "sec-websocket-accept",
        "sec-websocket-extensions",
        "sec-websocket-key",
        "sec-websocket-protocol",
        "sec-websocket-version",
        "security-scheme",
        "server",
        "server-timing",
        "set-cookie",
        "set-cookie2",
        "setprofile",
        "slug",
        "soapaction",
        "status-uri",
        "strict-transport-security",
        "sunset",
        "surrogate-capability",
        "surrogate-control",
        "tcn",
        "te",
        "timeout",
        "timing-allow-origin",
        "tk",
        "trailer",
        "transfer-encoding",
        "upgrade",
        "upgrade-insecure-requests",
        "uri",
        "user-agent",
        "variant-vary",
        "vary",
        "via",
        "want-digest",
        "warning",
        "www-authenticate",
        "x-att-deviceid",
        "x-csrf-token",
        "x-forwarded-for",
        "x-forwarded-host",
        "x-forwarded-proto",
        "x-frame-options",
        "x-frontend",
        "x-http-method-override",
        "x-powered-by",
        "x-request-id",
        "x-requested-with",
        "x-uidh",
        "x-wap-profile",
        "x-xss-protection"
    ];
    private static readonly string[] authHeaders =
    [
        "access-token",
        "api-key",
        "auth-token",
        "authorization",
        "authorization-token",
        "cookie",
        "key",
        "token",
        "x-access-token",
        "x-access-token",
        "x-api-key",
        "x-auth",
        "x-auth-token",
        "x-csrf-token",
        "secret",
        "x-secret",
        "access-key",
        "api-key",
        "apikey"
    ];

    public override string Name => nameof(OpenApiSpecGeneratorPlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    private Task AfterRecordingStop(object? sender, RecordingArgs e)
    {
        _logger?.LogInformation("Creating OpenAPI spec from recorded requests...");

        if (!e.RequestLogs.Any())
        {
            _logger?.LogDebug("No requests to process");
            return Task.CompletedTask;
        }

        var openApiDocs = new List<OpenApiDocument>();

        foreach (var request in e.RequestLogs)
        {
            if (request.MessageType != MessageType.InterceptedResponse ||
              request.Context is null ||
              request.Context.Session is null)
            {
                continue;
            }

            var methodAndUrlString = request.MessageLines.First();
            _logger?.LogDebug("Processing request {methodAndUrlString}...", methodAndUrlString);

            try
            {
                var pathItem = GetOpenApiPathItem(request.Context.Session);
                var parametrizedPath = ParametrizePath(pathItem, request.Context.Session.HttpClient.Request.RequestUri);
                var operationInfo = pathItem.Operations.First();
                operationInfo.Value.OperationId = GetOperationId(operationInfo.Key.ToString(), parametrizedPath);
                AddOrMergePathItem(openApiDocs, pathItem, request.Context.Session.HttpClient.Request.RequestUri, parametrizedPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing request {methodAndUrl}", methodAndUrlString);
            }
        }

        _logger?.LogDebug("Serializing OpenAPI docs...");
        foreach (var openApiDoc in openApiDocs)
        {
            var server = openApiDoc.Servers.First();
            var fileName = GetFileNameFromServerUrl(server.Url);
            var docString = openApiDoc.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0);

            _logger?.LogDebug("  Writing OpenAPI spec to {fileName}...", fileName);
            File.WriteAllText(fileName, docString);

            _logger?.LogInformation("Created OpenAPI spec file {fileName}", fileName);
        }

        return Task.CompletedTask;
    }

    /**
     * Replaces segments in the request URI, that match predefined patters,
     * with parameters and adds them to the OpenAPI PathItem.
     * @param pathItem The OpenAPI PathItem to parametrize.
     * @param requestUri The request URI.
     * @returns The parametrized server-relative URL
     */
    private string ParametrizePath(OpenApiPathItem pathItem, Uri requestUri)
    {
        var segments = requestUri.Segments;
        var previousSegment = "item";

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = requestUri.Segments[i].Trim('/');
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            if (IsParametrizable(segment))
            {
                var parameterName = $"{previousSegment}-id";
                segments[i] = "{" + parameterName + "}/";

                pathItem.Parameters.Add(new OpenApiParameter
                {
                    Name = parameterName,
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = "string" }
                });
            }
            else
            {
                previousSegment = segment;
            }
        }

        return string.Join(string.Empty, segments);
    }

    private bool IsParametrizable(string segment)
    {
        return Guid.TryParse(segment.Trim('/'), out _) ||
          int.TryParse(segment.Trim('/'), out _);
    }

    private string GetLastNonTokenSegment(string[] segments)
    {
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var segment = segments[i].Trim('/');
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            if (!IsParametrizable(segment))
            {
                return segment;
            }
        }

        return "item";
    }

    private string GetOperationId(string method, string parametrizedPath)
    {
        return $"{method}{parametrizedPath.Replace('/', '.')}";
    }

    /**
     * Creates an OpenAPI PathItem from an intercepted request and response pair.
     * @param session The intercepted session.
     */
    private OpenApiPathItem GetOpenApiPathItem(SessionEventArgs session)
    {
        var request = session.HttpClient.Request;
        var response = session.HttpClient.Response;

        var resource = GetLastNonTokenSegment(request.RequestUri.Segments);
        var path = new OpenApiPathItem
        {
            Description = $"Provides operations to manage {resource}"
        };

        var method = request.Method.ToUpperInvariant() switch
        {
            "DELETE" => OperationType.Delete,
            "GET" => OperationType.Get,
            "HEAD" => OperationType.Head,
            "OPTIONS" => OperationType.Options,
            "PATCH" => OperationType.Patch,
            "POST" => OperationType.Post,
            "PUT" => OperationType.Put,
            "TRACE" => OperationType.Trace,
            _ => throw new NotSupportedException($"Method {request.Method} is not supported")
        };
        var operation = new OpenApiOperation
        {
            Summary = $"{method} {resource}",
            // will be replaced later after the path has been parametrized
            OperationId = $"{method}.{resource}"
        };
        SetParametersFromQueryString(operation, HttpUtility.ParseQueryString(request.RequestUri.Query));
        SetParametersFromRequestHeaders(operation, request.Headers);
        SetRequestBody(operation, request);
        SetResponseFromSession(operation, response);

        path.Operations.Add(method, operation);

        return path;
    }

    private void SetRequestBody(OpenApiOperation operation, Request request)
    {
        if (!request.HasBody)
        {
            _logger?.LogDebug("  Request has no body");
            return;
        }

        if (request.ContentType is null)
        {
            _logger?.LogDebug("  Request has no content type");
            return;
        }

        _logger?.LogDebug("  Processing request body...");
        operation.RequestBody = new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                {
                    request.ContentType,
                    new OpenApiMediaType
                    {
                        Schema = GetSchemaFromBody(request.ContentType, request.BodyString)
                    }
                }
            }
        };
    }

    private void SetParametersFromRequestHeaders(OpenApiOperation operation, HeaderCollection headers)
    {
        if (headers is null ||
            !headers.Any())
        {
            _logger?.LogDebug("  Request has no headers");
            return;
        }

        _logger?.LogDebug("  Processing request headers...");
        foreach (var header in headers)
        {
            var lowerCaseHeaderName = header.Name.ToLowerInvariant();
            if (standardHeaders.Contains(lowerCaseHeaderName))
            {
                _logger?.LogDebug("    Skipping standard header {headerName}", header.Name);
                continue;
            }

            if (authHeaders.Contains(lowerCaseHeaderName))
            {
                _logger?.LogDebug("    Skipping auth header {headerName}", header.Name);
                continue;
            }

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = header.Name,
                In = ParameterLocation.Header,
                Required = false,
                Schema = new OpenApiSchema { Type = "string" }
            });
            _logger?.LogDebug("    Added header {headerName}", header.Name);
        }
    }

    private void SetParametersFromQueryString(OpenApiOperation operation, NameValueCollection queryParams)
    {
        if (queryParams.AllKeys is null ||
            !queryParams.AllKeys.Any())
        {
            _logger?.LogDebug("  Request has no query string parameters");
            return;
        }

        _logger?.LogDebug("  Processing query string parameters...");
        var dictionary = (queryParams.AllKeys as string[]).ToDictionary(k => k, k => queryParams[k] as object);

        foreach (var parameter in dictionary)
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = parameter.Key,
                In = ParameterLocation.Query,
                Required = false,
                Schema = new OpenApiSchema { Type = "string" }
            });
            _logger?.LogDebug("    Added query string parameter {parameterKey}", parameter.Key);
        }
    }

    private void SetResponseFromSession(OpenApiOperation operation, Response response)
    {
        if (response is null)
        {
            _logger?.LogDebug("  No response to process");
            return;
        }

        _logger?.LogDebug("  Processing response...");

        var openApiResponse = new OpenApiResponse
        {
            Description = response.StatusDescription
        };
        var responseCode = response.StatusCode.ToString();
        if (response.HasBody)
        {
            _logger?.LogDebug("    Response has body");

            openApiResponse.Content.Add(response.ContentType, new OpenApiMediaType
            {
                Schema = GetSchemaFromBody(response.ContentType, response.BodyString)
            });
        }
        else
        {
            _logger?.LogDebug("    Response doesn't have body");
        }

        if (response.Headers is not null && response.Headers.Any())
        {
            _logger?.LogDebug("    Response has headers");

            foreach (var header in response.Headers)
            {
                var lowerCaseHeaderName = header.Name.ToLowerInvariant();
                if (standardHeaders.Contains(lowerCaseHeaderName))
                {
                    _logger?.LogDebug("    Skipping standard header {headerName}", header.Name);
                    continue;
                }

                if (authHeaders.Contains(lowerCaseHeaderName))
                {
                    _logger?.LogDebug("    Skipping auth header {headerName}", header.Name);
                    continue;
                }

                if (openApiResponse.Headers.ContainsKey(header.Name))
                {
                    _logger?.LogDebug("    Header {headerName} already exists in response", header.Name);
                    continue;
                }

                openApiResponse.Headers.Add(header.Name, new OpenApiHeader
                {
                    Schema = new OpenApiSchema { Type = "string" }
                });
                _logger?.LogDebug("    Added header {headerName}", header.Name);
            }
        }
        else
        {
            _logger?.LogDebug("    Response doesn't have headers");
        }

        operation.Responses.Add(responseCode, openApiResponse);
    }

    private OpenApiSchema? GetSchemaFromBody(string? contentType, string body)
    {
        if (contentType is null)
        {
            _logger?.LogDebug("  No content type to process");
            return null;
        }

        if (contentType.StartsWith("application/json"))
        {
            _logger?.LogDebug("    Processing JSON body...");
            return GetSchemaFromJsonString(body);
        }

        return null;
    }

    private void AddOrMergePathItem(IList<OpenApiDocument> openApiDocs, OpenApiPathItem pathItem, Uri requestUri, string parametrizedPath)
    {
        var serverUrl = requestUri.GetLeftPart(UriPartial.Authority);
        var openApiDoc = openApiDocs.FirstOrDefault(d => d.Servers.Any(s => s.Url == serverUrl));

        if (openApiDoc is null)
        {
            _logger?.LogDebug("  Creating OpenAPI spec for {serverUrl}...", serverUrl);

            openApiDoc = new OpenApiDocument
            {
                Info = new OpenApiInfo
                {
                    Version = "v1.0",
                    Title = $"{serverUrl} API",
                    Description = $"{serverUrl} API",
                },
                Servers = new List<OpenApiServer>
                {
                    new OpenApiServer { Url = serverUrl }
                },
                Paths = new OpenApiPaths(),
                Extensions = new Dictionary<string, IOpenApiExtension>
                {
                    { "x-ms-generated-by", new GeneratedByOpenApiExtension() }
                }
            };
            openApiDocs.Add(openApiDoc);
        }
        else
        {
            _logger?.LogDebug("  Found OpenAPI spec for {serverUrl}...", serverUrl);
        }

        if (!openApiDoc.Paths.ContainsKey(parametrizedPath))
        {
            _logger?.LogDebug("  Adding path {parametrizedPath} to OpenAPI spec...", parametrizedPath);

            openApiDoc.Paths.Add(parametrizedPath, pathItem);
            // since we've just added the path, we're done
            return;
        }

        _logger?.LogDebug("  Merging path {parametrizedPath} into OpenAPI spec...", parametrizedPath);
        var path = openApiDoc.Paths[parametrizedPath];
        var operation = pathItem.Operations.First();
        AddOrMergeOperation(path, operation.Key, operation.Value);
    }

    private void AddOrMergeOperation(OpenApiPathItem pathItem, OperationType operationType, OpenApiOperation apiOperation)
    {
        if (!pathItem.Operations.ContainsKey(operationType))
        {
            _logger?.LogDebug("    Adding operation {operationType} to path...", operationType);

            pathItem.AddOperation(operationType, apiOperation);
            // since we've just added the operation, we're done
            return;
        }

        _logger?.LogDebug("    Merging operation {operationType} into path...", operationType);

        var operation = pathItem.Operations[operationType];

        AddOrMergeParameters(operation, apiOperation.Parameters);
        AddOrMergeRequestBody(operation, apiOperation.RequestBody);
        AddOrMergeResponse(operation, apiOperation.Responses);
    }

    private void AddOrMergeParameters(OpenApiOperation operation, IList<OpenApiParameter> parameters)
    {
        if (parameters is null || !parameters.Any())
        {
            _logger?.LogDebug("    No parameters to process");
            return;
        }

        _logger?.LogDebug("    Processing parameters for operation...");

        foreach (var parameter in parameters)
        {
            var paramFromOperation = operation.Parameters.FirstOrDefault(p => p.Name == parameter.Name && p.In == parameter.In);
            if (paramFromOperation is null)
            {
                _logger?.LogDebug("      Adding parameter {parameterName} to operation...", parameter.Name);
                operation.Parameters.Add(parameter);
                continue;
            }

            _logger?.LogDebug("      Merging parameter {parameterName}...", parameter.Name);
            MergeSchema(parameter?.Schema, paramFromOperation?.Schema);
        }
    }

    private void MergeSchema(OpenApiSchema? source, OpenApiSchema? target)
    {
        if (source is null || target is null)
        {
            _logger?.LogDebug("        Source or target is null. Skipping...");
            return;
        }

        if (source.Type != "object" || target.Type != "object")
        {
            _logger?.LogDebug("        Source or target schema is not an object. Skipping...");
            return;
        }

        if (source.Properties is null || !source.Properties.Any())
        {
            _logger?.LogDebug("        Source has no properties. Skipping...");
            return;
        }

        if (target.Properties is null || !target.Properties.Any())
        {
            _logger?.LogDebug("        Target has no properties. Skipping...");
            return;
        }

        foreach (var property in source.Properties)
        {
            var propertyFromTarget = target.Properties.FirstOrDefault(p => p.Key == property.Key);
            if (propertyFromTarget.Value is null)
            {
                _logger?.LogDebug("        Adding property {propertyKey} to schema...", property.Key);
                target.Properties.Add(property);
                continue;
            }

            if (property.Value.Type != "object")
            {
                _logger?.LogDebug("        Property already found but is not an object. Skipping...");
                continue;
            }

            _logger?.LogDebug("        Merging property {propertyKey}...", property.Key);
            MergeSchema(property.Value, propertyFromTarget.Value);
        }
    }

    private void AddOrMergeRequestBody(OpenApiOperation operation, OpenApiRequestBody requestBody)
    {
        if (requestBody is null || !requestBody.Content.Any())
        {
            _logger?.LogDebug("    No request body to process");
            return;
        }

        var requestBodyType = requestBody.Content.FirstOrDefault().Key;
        var bodyFromOperation = operation.RequestBody.Content.ContainsKey(requestBodyType) ?
          operation.RequestBody.Content[requestBodyType] : null;

        if (bodyFromOperation is null)
        {
            _logger?.LogDebug("    Adding request body to operation...");

            operation.RequestBody.Content.Add(requestBody.Content.FirstOrDefault());
            // since we've just added the request body, we're done
            return;
        }

        _logger?.LogDebug("    Merging request body into operation...");
        MergeSchema(bodyFromOperation.Schema, requestBody.Content.FirstOrDefault().Value.Schema);
    }

    private void AddOrMergeResponse(OpenApiOperation operation, OpenApiResponses apiResponses)
    {
        if (apiResponses is null)
        {
            _logger?.LogDebug("    No response to process");
            return;
        }

        var apiResponseInfo = apiResponses.FirstOrDefault();
        var apiResponseStatusCode = apiResponseInfo.Key;
        var apiResponse = apiResponseInfo.Value;
        var responseFromOperation = operation.Responses.ContainsKey(apiResponseStatusCode) ?
          operation.Responses[apiResponseStatusCode] : null;

        if (responseFromOperation is null)
        {
            _logger?.LogDebug("    Adding response {apiResponseStatusCode} to operation...", apiResponseStatusCode);

            operation.Responses.Add(apiResponseStatusCode, apiResponse);
            // since we've just added the response, we're done
            return;
        }

        if (!apiResponse.Content.Any())
        {
            _logger?.LogDebug("    No response content to process");
            return;
        }

        var apiResponseContentType = apiResponse.Content.First().Key;
        var contentFromOperation = responseFromOperation.Content.ContainsKey(apiResponseContentType) ?
          responseFromOperation.Content[apiResponseContentType] : null;

        if (contentFromOperation is null)
        {
            _logger?.LogDebug("    Adding response {apiResponseContentType} to {apiResponseStatusCode} to response...", apiResponseContentType, apiResponseStatusCode);

            responseFromOperation.Content.Add(apiResponse.Content.First());
            // since we've just added the content, we're done
            return;
        }

        _logger?.LogDebug("    Merging response {apiResponseStatusCode}/{apiResponseContentType} into operation...", apiResponseStatusCode, apiResponseContentType);
        MergeSchema(contentFromOperation.Schema, apiResponse.Content.First().Value.Schema);
    }

    private string GetFileNameFromServerUrl(string serverUrl)
    {
        var uri = new Uri(serverUrl);
        var fileName = $"{uri.Host}-{DateTime.Now:yyyyMMddHHmmss}.json";
        return fileName;
    }

    private OpenApiSchema GetSchemaFromJsonString(string jsonString)
    {
        try
        {
            using (JsonDocument doc = JsonDocument.Parse(jsonString))
            {
                JsonElement root = doc.RootElement;
                var schema = GetSchemaFromJsonElement(root);
                return schema;
            }
        }
        catch
        {
            return new OpenApiSchema
            {
                Type = "object"
            };
        }
    }

    private OpenApiSchema GetSchemaFromJsonElement(JsonElement jsonElement)
    {
        var schema = new OpenApiSchema();

        switch (jsonElement.ValueKind)
        {
            case JsonValueKind.String:
                schema.Type = "string";
                break;
            case JsonValueKind.Number:
                schema.Type = "number";
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                schema.Type = "boolean";
                break;
            case JsonValueKind.Object:
                schema.Type = "object";
                schema.Properties = jsonElement.EnumerateObject()
                  .ToDictionary(p => p.Name, p => GetSchemaFromJsonElement(p.Value));
                break;
            case JsonValueKind.Array:
                schema.Type = "array";
                schema.Items = GetSchemaFromJsonElement(jsonElement.EnumerateArray().FirstOrDefault());
                break;
            default:
                schema.Type = "object";
                break;
        }

        return schema;
    }
}
