// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Abstractions;

public abstract class BaseProxyPlugin : IProxyPlugin
{
    protected ISet<UrlToWatch> UrlsToWatch { get; }
    protected ILogger Logger { get; }
    protected IConfigurationSection? ConfigSection { get; }
    protected IPluginEvents PluginEvents { get; }
    protected IProxyContext Context { get; }

    public virtual string Name => throw new NotImplementedException();

    public virtual Option[] GetOptions() => Array.Empty<Option>();
    public virtual Command[] GetCommands() => Array.Empty<Command>();

    public BaseProxyPlugin(IPluginEvents pluginEvents,
                         IProxyContext context,
                         ILogger logger,
                         ISet<UrlToWatch> urlsToWatch,
                         IConfigurationSection? configSection = null)
    {
        ArgumentNullException.ThrowIfNull(pluginEvents);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        if (urlsToWatch is null || !urlsToWatch.Any())
        {
            throw new ArgumentException($"{nameof(urlsToWatch)} cannot be null or empty", nameof(urlsToWatch));
        }

        UrlsToWatch = urlsToWatch;
        Context = context;
        Logger = logger;
        ConfigSection = configSection;
        PluginEvents = pluginEvents;
    }

    public virtual Task RegisterAsync()
    {
        return Task.CompletedTask;
    }
}
