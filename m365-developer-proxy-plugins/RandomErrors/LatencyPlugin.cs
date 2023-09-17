// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft365.DeveloperProxy.Abstractions;

namespace Microsoft365.DeveloperProxy.Plugins.RandomErrors;

public class LatencyConfiguration {
    public int MinMs { get; set; } = 0;
    public int MaxMs { get; set; } = 5000;
}

public class LatencyPlugin : BaseProxyPlugin {
    private readonly LatencyConfiguration _configuration = new();

    public override string Name => nameof(LatencyPlugin);
    private readonly Random _random;

    public LatencyPlugin() {
        _random = new Random();
    }

    public override void Register(IPluginEvents pluginEvents,
                         IProxyContext context,
                         ISet<UrlToWatch> urlsToWatch,
                         IConfigurationSection? configSection = null) {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        configSection?.Bind(_configuration);
        pluginEvents.BeforeRequest += OnRequest;
    }

    private async Task OnRequest(object? sender, ProxyRequestArgs e) {
        if (_urlsToWatch is not null
            && e.ShouldExecute(_urlsToWatch)) {
            var delay = _random.Next(_configuration.MinMs, _configuration.MaxMs);
            _logger?.LogRequest(new[] { $"Delaying request for {delay}ms" }, MessageType.Chaos, new LoggingContext(e.Session));
            await Task.Delay(delay);
        }
    }
}
