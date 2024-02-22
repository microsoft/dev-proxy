// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using Microsoft.Extensions.Configuration;

namespace Microsoft.DevProxy.Abstractions;

public abstract class BaseProxyPlugin : IProxyPlugin
{
    protected ISet<UrlToWatch>? _urlsToWatch;
    protected IProxyLogger? _logger;

    public virtual string Name => throw new NotImplementedException();

    public virtual Option[] GetOptions() => Array.Empty<Option>();
    public virtual Command[] GetCommands() => Array.Empty<Command>();

    public virtual void Register(IPluginEvents pluginEvents,
                         IProxyContext context,
                         ISet<UrlToWatch> urlsToWatch,
                         IConfigurationSection? configSection = null)
    {
        if (pluginEvents is null)
        {
            throw new ArgumentNullException(nameof(pluginEvents));
        }

        if (context is null || context.Logger is null)
        {
            throw new ArgumentException($"{nameof(context)} must not be null and must supply a non-null Logger", nameof(context));

        }

        if (urlsToWatch is null || urlsToWatch.Count == 0)
        {
            throw new ArgumentException($"{nameof(urlsToWatch)} cannot be null or empty", nameof(urlsToWatch));
        }

        _urlsToWatch = urlsToWatch;
        _logger = context.Logger;
    }
}
