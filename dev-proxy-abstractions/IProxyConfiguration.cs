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
    int ApiPort { get; }
    bool AsSystemProxy { get; }
    string? IPAddress { get; }
    string ConfigFile { get; }
    bool InstallCert { get; }
    MockRequestHeader[]? FilterByHeaders { get; }
    LabelMode LabelMode { get; }
    LogLevel LogLevel { get; }
    bool NoFirstRun { get; }
    int Port { get; }
    int Rate { get; }
    bool Record { get; }
    IEnumerable<int> WatchPids { get; }
    IEnumerable<string> WatchProcessNames { get; }
}