// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.DevProxy.Abstractions;

public class MockResponse
{
    [JsonPropertyName("request")]
    public MockResponseRequest? Request { get; set; }
    [JsonPropertyName("response")]
    public MockResponseResponse? Response { get; set; }
}

public class MockResponseRequest
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";
    [JsonPropertyName("nth"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Nth { get; set; }
}

public class MockResponseResponse
{
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; } = 200;
    [JsonPropertyName("body")]
    public dynamic? Body { get; set; }
    [JsonPropertyName("headers")]
    public List<MockResponseHeader>? Headers { get; set; }
}

public class MockResponseHeader
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    public MockResponseHeader()
    {
    }

    public MockResponseHeader(string name, string value)
    {
        Name = name;
        Value = value;
    }
}