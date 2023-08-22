// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft365.DeveloperProxy.Abstractions;

class ParsedSample {
    public string QueryVersion { get; set; } = string.Empty;
    public string RequestUrl { get; set; } = string.Empty;
    public string SampleUrl { get; set; } = string.Empty;
    public string Search { get; set; } = string.Empty;
}

public static class ProxyUtils {
    private static readonly Regex itemPathRegex = new Regex(@"(?:\/)[\w]+:[\w\/.]+(:(?=\/)|$)");
    private static readonly Regex sanitizedItemPathRegex = new Regex("^[a-z]+:<value>$", RegexOptions.IgnoreCase);
    private static readonly Regex entityNameRegex = new Regex("^((microsoft.graph(.[a-z]+)+)|[a-z]+)$", RegexOptions.IgnoreCase);
    private static readonly Regex allAlphaRegex = new Regex("^[a-z]+$", RegexOptions.IgnoreCase);
    private static readonly Regex deprecationRegex = new Regex("^[a-z]+_v2$", RegexOptions.IgnoreCase);
    private static readonly Regex functionCallRegex = new Regex(@"^[a-z]+\(.*\)$", RegexOptions.IgnoreCase);

    public static string MsGraphDbFilePath => Path.Combine(AppFolder!, "msgraph-openapi-v1.db");
    private static SqliteConnection? _msGraphDbConnection;
    public static SqliteConnection MsGraphDbConnection {
        get {
            if (_msGraphDbConnection is null) {
                // v1 refers to v1 of the db schema, not the graph version
                _msGraphDbConnection = new SqliteConnection($"Data Source={MsGraphDbFilePath}");
                _msGraphDbConnection.Open();
            }

            return _msGraphDbConnection;
        }
    }

    // doesn't end with a path separator
    public static string? AppFolder => Path.GetDirectoryName(AppContext.BaseDirectory);

    public static bool IsGraphRequest(Request request) => IsGraphUrl(request.RequestUri);

    public static bool IsGraphUrl(Uri uri) => 
        uri.Host.StartsWith("graph.microsoft.", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.StartsWith("microsoftgraph.", StringComparison.OrdinalIgnoreCase);

    public static bool IsGraphBatchUrl(Uri uri) => 
        uri.AbsoluteUri.EndsWith("/$batch", StringComparison.OrdinalIgnoreCase);

    public static Uri GetAbsoluteRequestUrlFromBatch(Uri batchRequestUri, string relativeRequestUrl) {
        var hostName = batchRequestUri.Host;
        var graphVersion = batchRequestUri.Segments[1].TrimEnd('/');
        var absoluteRequestUrl = new Uri($"https://{hostName}/{graphVersion}{relativeRequestUrl}");
        return absoluteRequestUrl;
    }

    public static bool IsSdkRequest(Request request) => request.Headers.HeaderExists("SdkVersion");

    public static bool IsGraphBetaRequest(Request request) => 
        IsGraphRequest(request) &&
        IsGraphBetaUrl(request.RequestUri);

    public static bool IsGraphBetaUrl(Uri uri) => 
        uri.AbsolutePath.Contains("/beta/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Utility to build HTTP response headers consistent with Microsoft Graph
    /// </summary>
    /// <param name="request">The http request for which response headers are being constructed</param>
    /// <param name="requestId">string a guid representing the a unique identifier for the request</param>
    /// <param name="requestDate">string representation of the date and time the request was made</param>
    /// <returns>IList<HttpHeader> with defaults consistent with Microsoft Graph. Automatically adds CORS headers when the Origin header is present</returns>
    public static IList<HttpHeader> BuildGraphResponseHeaders(Request request, string requestId, string requestDate) {
        var headers = new List<HttpHeader>
            {
                new HttpHeader("Cache-Control", "no-store"),
                new HttpHeader("x-ms-ags-diagnostic", ""),
                new HttpHeader("Strict-Transport-Security", ""),
                new HttpHeader("request-id", requestId),
                new HttpHeader("client-request-id", requestId),
                new HttpHeader("Date", requestDate)
            };
        if (request.Headers.FirstOrDefault((h) => h.Name.Equals("Origin", StringComparison.OrdinalIgnoreCase)) is not null) {
            headers.Add(new HttpHeader("Access-Control-Allow-Origin", "*"));
            headers.Add(new HttpHeader("Access-Control-Expose-Headers", "ETag, Location, Preference-Applied, Content-Range, request-id, client-request-id, ReadWriteConsistencyToken, SdkVersion, WWW-Authenticate, x-ms-client-gcc-tenant, Retry-After"));
        }
        return headers;
    }

    public static string ReplacePathTokens(string? path) {
        if (string.IsNullOrEmpty(path)) {
            return path ?? string.Empty;
        }

        return path.Replace("~appFolder", AppFolder, StringComparison.OrdinalIgnoreCase);
    }

    // from: https://github.com/microsoftgraph/microsoft-graph-explorer-v4/blob/db86b903f36ef1b882996d46aee52cd49ed4444b/src/app/utils/query-url-sanitization.ts
    public static string SanitizeUrl(string absoluteUrl) {
        absoluteUrl = Uri.UnescapeDataString(absoluteUrl);
        var uri = new Uri(absoluteUrl);

        var parsedSample = ParseSampleUrl(absoluteUrl);
        var queryString = !String.IsNullOrEmpty(parsedSample.Search) ? $"?{SanitizeQueryParameters(parsedSample.Search)}" : "";

        // Sanitize item path specified in query url
        var resourceUrl = parsedSample.RequestUrl;
        if (!String.IsNullOrEmpty(resourceUrl)) {
            resourceUrl = itemPathRegex.Replace(parsedSample.RequestUrl, match => {
                return $"{match.Value.Substring(0, match.Value.IndexOf(':'))}:<value>";
            });
            // Split requestUrl into segments that can be sanitized individually
            var urlSegments = resourceUrl.Split('/');
            for (var i = 0; i < urlSegments.Length; i++) {
                var segment = urlSegments[i];
                var sanitizedSegment = SanitizePathSegment(i < 1 ? "" :  urlSegments[i - 1], segment);
                resourceUrl = resourceUrl.Replace(segment, sanitizedSegment);
            }
        }
        return $"{uri.GetLeftPart(UriPartial.Authority)}/{parsedSample.QueryVersion}/{resourceUrl}{queryString}";
    }

    /**
    * Skipped segments:
    * - Entities, entity sets and navigation properties, expected to contain alphabetic letters only
    * - Deprecated entities in the form <entity>_v2
    * The remaining URL segments are assumed to be variables that need to be sanitized
    * @param segment
    */
    private static string SanitizePathSegment(string previousSegment, string segment) {
        var segmentsToIgnore = new[] { "$value", "$count", "$ref", "$batch" };

        if (IsAllAlpha(segment) ||
            IsDeprecation(segment) ||
            sanitizedItemPathRegex.IsMatch(segment) ||
            segmentsToIgnore.Contains(segment.ToLowerInvariant()) ||
            entityNameRegex.IsMatch(segment)) {
            return segment;
        }

        // Check if segment is in this form: users('<some-id>|<UPN>') and transform to users(<value>)
        if (IsFunctionCall(segment)) {
            var openingBracketIndex = segment.IndexOf("(");
            var textWithinBrackets = segment.Substring(
                openingBracketIndex + 1,
                segment.Length - 2
            );
            var sanitizedText = String.Join(',', textWithinBrackets
                .Split(',')
                .Select(text => {
                    if (text.Contains('=')) {
                        var key = text.Split('=')[0];
                        key = !IsAllAlpha(key) ? "<key>" : key;
                        return $"{key}=<value>";
                    }
                    return "<value>";
                }));

            return $"{segment.Substring(0, openingBracketIndex)}({sanitizedText})";
        }

        if (IsPlaceHolderSegment(segment)) {
            return segment;
        }

        if (!IsAllAlpha(previousSegment) && !IsDeprecation(previousSegment)) {
            previousSegment = "unknown";
        }

        return $"{{{previousSegment}-id}}";
    }

    private static string SanitizeQueryParameters(string queryString) {
        // remove leading ? from query string and decode
        queryString = Uri.UnescapeDataString(
            new Regex(@"\+").Replace(queryString.Substring(1), " ")
        );
        return String.Join('&', queryString.Split('&').Select(s => s));
    }

    private static bool IsAllAlpha(string value) => allAlphaRegex.IsMatch(value);

    private static bool IsDeprecation(string value) => deprecationRegex.IsMatch(value);

    private static bool IsFunctionCall(string value) => functionCallRegex.IsMatch(value);

    private static bool IsPlaceHolderSegment(string segment) {
        return segment.StartsWith('{') && segment.EndsWith('}');
    }

    private static ParsedSample ParseSampleUrl(string url, string? version = null) {
        var parsedSample = new ParsedSample();

        if (url != "") {
            try {
                url = RemoveExtraSlashesFromUrl(url);
                parsedSample.QueryVersion = version ?? GetGraphVersion(url);
                parsedSample.RequestUrl = GetRequestUrl(url, parsedSample.QueryVersion);
                parsedSample.Search = GenerateSearchParameters(url, "");
                parsedSample.SampleUrl = GenerateSampleUrl(url, parsedSample.QueryVersion, parsedSample.RequestUrl, parsedSample.Search);
            } catch (Exception) { }
        }

        return parsedSample;
    }

    private static string RemoveExtraSlashesFromUrl(string url) {
        return new Regex(@"([^:]\/)\/+").Replace(url, "$1");
    }

    public static string GetGraphVersion(string url) {
        var uri = new Uri(url);
        return uri.Segments[1].Replace("/", "");
    }

    private static string GetRequestUrl(string url, string version) {
        var uri = new Uri(url);
        var versionToReplace = uri.AbsolutePath.StartsWith($"/{version}")
            ? version
            : GetGraphVersion(url);
        var requestContent = uri.AbsolutePath.Split(versionToReplace).LastOrDefault() ?? "";
        return Uri.UnescapeDataString(requestContent.TrimEnd('/')).TrimStart('/');
    }

    private static string GenerateSearchParameters(string url, string search) {
        var uri = new Uri(url);

        if (uri.Query != "") {
            try {
             search = Uri.UnescapeDataString(uri.Query);
            } catch (Exception) {
                search = uri.Query;
            }
        }

        return new Regex(@"\s").Replace(search, "+");
    }

    private static string GenerateSampleUrl(
        string url,
        string queryVersion,
        string requestUrl,
        string search
    ) {
        var uri = new Uri(url);
        var origin = uri.GetLeftPart(UriPartial.Authority);
        return RemoveExtraSlashesFromUrl($"{origin}/{queryVersion}/{requestUrl + search}");
    }
}
