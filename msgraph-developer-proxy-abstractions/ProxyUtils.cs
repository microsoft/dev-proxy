// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.Graph.DeveloperProxy.Abstractions;

public static class ProxyUtils {
    public static bool IsGraphRequest(Request request) => 
        request.RequestUri.Host.Contains("graph.microsoft.", StringComparison.OrdinalIgnoreCase) ||
        request.RequestUri.Host.Contains("microsoftgraph.", StringComparison.OrdinalIgnoreCase);

    public static bool IsSdkRequest(Request request) => request.Headers.HeaderExists("SdkVersion");

    /// <summary>
    /// Utiliy to build HTTP respopnse headers consistent with Microsoft Graph
    /// </summary>
    /// <param name="request">The http request for which response headers are being constructed</param>
    /// <param name="requestId">string a guid representing the a unique identifier for the request</param>
    /// <param name="requestDate">string represetation of the date and time the request was made</param>
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
}
