// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.DevProxy.Abstractions;

public class MockRequest
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";
    [JsonPropertyName("body")]
    public dynamic? Body { get; set; }
    [JsonPropertyName("headers")]
    public List<MockRequestHeader>? Headers { get; set; }
}

public class MockRequestHeader
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    public MockRequestHeader()
    {
    }

    public MockRequestHeader(string name, string value)
    {
        Name = name;
        Value = value;
    }
}