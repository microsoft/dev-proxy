// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace Microsoft.Graph.DeveloperProxy.Abstractions;

public interface IProxyPlugin {
    string Name { get; }
    void Register(IPluginEvents pluginEvents,
                  IProxyContext context,
                  ISet<Regex> urlsToWatch,
                  IConfigurationSection? configSection = null);
}
