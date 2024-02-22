// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using Microsoft.Extensions.Configuration;

namespace Microsoft.DevProxy.Abstractions;

public interface IProxyPlugin
{
    string Name { get; }
    Option[] GetOptions();
    Command[] GetCommands();
    void Register(IPluginEvents pluginEvents,
                  IProxyContext context,
                  ISet<UrlToWatch> urlsToWatch,
                  IConfigurationSection? configSection = null);
}
