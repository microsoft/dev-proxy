// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions;

public class GraphBatchRequestPayload
{
    public GraphBatchRequestPayloadRequest[] Requests { get; set; } = [];
}

public class GraphBatchRequestPayloadRequest
{
    public string Id { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public Dictionary<string, string>? Headers { get; set; } = [];
    public object? Body { get; set; }
}