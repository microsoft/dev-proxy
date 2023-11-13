// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.DevProxy.Abstractions;

public class GraphBatchRequestPayload {
    [JsonPropertyName("requests")]
    public GraphBatchRequestPayloadRequest[] Requests { get; set; } = Array.Empty<GraphBatchRequestPayloadRequest>();
}

public class GraphBatchRequestPayloadRequest {
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; } = new Dictionary<string, string>();
    [JsonPropertyName("body")]
    public object? Body { get; set; }
}