// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft365.DeveloperProxy.Plugins.MockResponses;

public class MockResponse {
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";
    [JsonPropertyName("nth")]
    public int? Nth { get; set; }
    [JsonPropertyName("responseCode")]
    public int? ResponseCode { get; set; } = 200;
    [JsonPropertyName("responseBody")]
    public dynamic? ResponseBody { get; set; }
    [JsonPropertyName("responseHeaders")]
    public List<Dictionary<string, string>>? ResponseHeaders { get; set; }
}
