// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Microsoft.OpenApi.Models;

public static class OpenApiDocumentExtensions
{
    public static KeyValuePair<string, OpenApiPathItem>? FindMatchingPathItem(this OpenApiDocument openApiDocument, string requestUrl, ILogger logger)
    {
        foreach (var server in openApiDocument.Servers)
        {
            logger.LogDebug("Checking server URL {serverUrl}...", server.Url);

            if (!requestUrl.StartsWith(server.Url, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("Request URL {requestUrl} does not match server URL {serverUrl}", requestUrl, server.Url);
                continue;
            }

            var serverUrl = new Uri(server.Url);
            var serverPath = serverUrl.AbsolutePath.TrimEnd('/');
            var requestUri = new Uri(requestUrl);
            var urlPathFromRequest = requestUri.GetLeftPart(UriPartial.Path).Replace(server.Url.TrimEnd('/'), "", StringComparison.OrdinalIgnoreCase);

            foreach (var path in openApiDocument.Paths)
            {
                var urlPathFromSpec = path.Key;
                logger.LogDebug("Checking path {urlPath}...", urlPathFromSpec);

                // check if path contains parameters. If it does,
                // replace them with regex
                if (urlPathFromSpec.Contains('{'))
                {
                    logger.LogDebug("Path {urlPath} contains parameters and will be converted to Regex", urlPathFromSpec);

                    // force replace all parameters with regex
                    // this is more robust than replacing parameters by name
                    // because it's possible to define parameters both on the path
                    // and operations and sometimes, parameters are defined only
                    // on the operation. This way, we cover all cases, and we don't
                    // care about the parameter anyway here
                    urlPathFromSpec = Regex.Replace(urlPathFromSpec, @"\{[^}]+\}", $"([^/]+)");

                    logger.LogDebug("Converted path to Regex: {urlPath}", urlPathFromSpec);
                    var regex = new Regex($"^{urlPathFromSpec}$");
                    if (regex.IsMatch(urlPathFromRequest))
                    {
                        logger.LogDebug("Regex matches {requestUrl}", urlPathFromRequest);

                        return path;
                    }

                    logger.LogDebug("Regex does not match {requestUrl}", urlPathFromRequest);
                }
                else
                {
                    if (urlPathFromRequest.Equals(urlPathFromSpec, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogDebug("{requestUrl} matches {urlPath}", requestUrl, urlPathFromSpec);
                        return path;
                    }

                    logger.LogDebug("{requestUrl} doesn't match {urlPath}", requestUrl, urlPathFromSpec);
                }
            }
        }

        return null;
    }

    public static string[] GetEffectiveScopes(this OpenApiOperation operation, OpenApiDocument openApiDocument, ILogger logger)
    {
        var oauth2Scheme = openApiDocument.GetOAuth2Schemes().FirstOrDefault();
        if (oauth2Scheme is null)
        {
            logger.LogDebug("No OAuth2 schemes found in OpenAPI document");
            return [];
        }

        var globalScopes = new string[] { };
        var globalOAuth2Requirement = openApiDocument.SecurityRequirements
            .FirstOrDefault(req => req.ContainsKey(oauth2Scheme));
        if (globalOAuth2Requirement is not null)
        {
            globalScopes = [.. globalOAuth2Requirement[oauth2Scheme]];
        }

        if (operation.Security is null)
        {
            logger.LogDebug("No security requirements found in operation {operation}", operation.OperationId);
            return globalScopes;
        }

        var operationOAuth2Requirement = operation.Security
            .Where(req => req.ContainsKey(oauth2Scheme))
            .SelectMany(req => req[oauth2Scheme]);
        if (operationOAuth2Requirement is not null)
        {
            return operationOAuth2Requirement.ToArray();
        }

        return [];
    }

    public static OpenApiSecurityScheme[] GetOAuth2Schemes(this OpenApiDocument openApiDocument)
    {
        return openApiDocument.Components.SecuritySchemes
            .Where(s => s.Value.Type == SecuritySchemeType.OAuth2)
            .Select(s => s.Value)
            .ToArray();
    }
}