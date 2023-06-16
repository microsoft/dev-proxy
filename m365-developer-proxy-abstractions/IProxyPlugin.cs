// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;

namespace Microsoft365.DeveloperProxy.Abstractions;

public interface IProxyPlugin {
    string Name { get; }
    void Register(IPluginEvents pluginEvents,
                  IProxyContext context,
                  ISet<UrlToWatch> urlsToWatch,
                  IConfigurationSection? configSection = null);
}
