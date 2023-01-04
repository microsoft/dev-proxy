// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.Graph.DeveloperProxy;

public class ProxyConfiguration {
    [JsonPropertyName("port")]
    public int Port { get; set; } = 8000;
}

