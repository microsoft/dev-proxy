// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Abstractions;

public enum LabelMode
{
    [EnumMember(Value = "text")]
    Text,
    [EnumMember(Value = "icon")]
    Icon,
    [EnumMember(Value = "nerdFont")]
    NerdFont
}

public interface IProxyConfiguration
{
    int Port { get; }
    string? IPAddress { get; }
    LabelMode LabelMode { get; }
    bool Record { get; }
    LogLevel LogLevel { get; }
    IEnumerable<int> WatchPids { get; }
    IEnumerable<string> WatchProcessNames { get; }
    int Rate { get; }
    string ConfigFile { get; }
}