// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.DevProxy.Plugins;

public class GraphErrorResponseBody
{
    public GraphErrorResponseError Error { get; set; }

    public GraphErrorResponseBody(GraphErrorResponseError error)
    {
        Error = error;
    }
}

public class GraphErrorResponseError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public GraphErrorResponseInnerError? InnerError { get; set; }
}

public class GraphErrorResponseInnerError
{
    [JsonPropertyName("request-id")]
    public string RequestId { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
}
