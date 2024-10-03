// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.DevProxy.Abstractions;

namespace Microsoft.DevProxy.Plugins.RandomErrors;

public class LatencyConfiguration
{
    public int MinMs { get; set; } = 0;
    public int MaxMs { get; set; } = 5000;
}

public class LatencyPlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : BaseProxyPlugin(pluginEvents, context, logger, urlsToWatch, configSection)
{
    private readonly LatencyConfiguration _configuration = new();

    public override string Name => nameof(LatencyPlugin);
    private readonly Random _random = new();

    public override async Task RegisterAsync()
    {
        await base.RegisterAsync();

        ConfigSection?.Bind(_configuration);
        PluginEvents.BeforeRequest += OnRequestAsync;
    }

    private async Task OnRequestAsync(object? sender, ProxyRequestArgs e)
    {
        if (UrlsToWatch is null ||
            !e.ShouldExecute(UrlsToWatch))
        {
            Logger.LogRequest("URL not matched", MessageType.Skipped, new LoggingContext(e.Session));
            return;
        }

        var delay = _random.Next(_configuration.MinMs, _configuration.MaxMs);
        Logger.LogRequest($"Delaying request for {delay}ms", MessageType.Chaos, new LoggingContext(e.Session));
        await Task.Delay(delay);
    }
}
