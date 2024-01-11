// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Microsoft.DevProxy.Abstractions;

namespace Microsoft.DevProxy.Plugins;

public class GenericErrorResponse
{
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }
    [JsonPropertyName("headers")]
    public List<MockResponseHeader>? Headers { get; set; }
    [JsonPropertyName("body")]
    public dynamic? Body { get; set; }
}