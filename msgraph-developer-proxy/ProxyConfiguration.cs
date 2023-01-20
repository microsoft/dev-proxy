// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Microsoft.Graph.DeveloperProxy.Abstractions;

namespace Microsoft.Graph.DeveloperProxy;

public enum LabelMode {
    [EnumMember(Value = "text")]
    Text,
    [EnumMember(Value = "icon")]
    Icon,
    [EnumMember(Value = "nerdFont")]
    NerdFont
}

public class ProxyConfiguration {
    [JsonPropertyName("port")]
    public int Port { get; set; } = 8000;
    [JsonPropertyName("labelMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LabelMode LabelMode { get; set; } = LabelMode.Text;
    [JsonPropertyName("logLevel")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Info;
}

