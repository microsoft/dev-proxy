// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.DevProxy.Plugins.MockResponses;

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
    public List<KeyValuePair<string, string>>? Headers { get; set; }
}