// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.DevProxy.Plugins;

public class GraphErrorResponseBody
{
    [JsonPropertyName("error")]
    public GraphErrorResponseError Error { get; set; }

    public GraphErrorResponseBody(GraphErrorResponseError error)
    {
        Error = error;
    }
}

public class GraphErrorResponseError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("innerError")]
    public GraphErrorResponseInnerError? InnerError { get; set; }
}

public class GraphErrorResponseInnerError
{
    [JsonPropertyName("request-id")]
    public string RequestId { get; set; } = string.Empty;
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;
}
