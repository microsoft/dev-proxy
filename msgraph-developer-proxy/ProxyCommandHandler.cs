// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.RegularExpressions;

namespace Microsoft.Graph.DeveloperProxy;

public class ProxyCommandHandler : ICommandHandler {
    public Option<int> Port { get; set; }

    private readonly PluginEvents _pluginEvents;
    private readonly ISet<Regex> _urlsToWatch;
    private readonly ILogger _logger;
    public ProxyCommandHandler(Option<int> port,
                               PluginEvents pluginEvents,
                               ISet<Regex> urlsToWatch,
                               ILogger logger) {
        Port = port ?? throw new ArgumentNullException(nameof(port));
        _pluginEvents = pluginEvents ?? throw new ArgumentNullException(nameof(pluginEvents));
        _urlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Invoke(InvocationContext context) {
        return InvokeAsync(context).GetAwaiter().GetResult();
    }

    public async Task<int> InvokeAsync(InvocationContext context) {
        int port = context.ParseResult.GetValueForOption(Port);
        CancellationToken? cancellationToken = (CancellationToken?)context.BindingContext.GetService(typeof(CancellationToken?));
        Configuration.Port = port;

        _pluginEvents.FireOptionsLoaded(new OptionsLoadedArgs(context));

        var newReleaseInfo = await UpdateNotification.CheckForNewVersion();
        if (newReleaseInfo != null) {
            _logger.LogError($"New version {newReleaseInfo.Version} of the Graph Developer Proxy is available.");
            _logger.LogError($"See {newReleaseInfo.Url} for more information.");
            _logger.LogError(string.Empty);
        }

        try {
            await new ChaosEngine(Configuration, _urlsToWatch, _pluginEvents, _logger).Run(cancellationToken);
            return 0;
        }
        catch (Exception ex) {
            _logger.LogError("An error occurred while running the Developer Proxy");
            _logger.LogError(ex.Message.ToString());
            _logger.LogError(ex.StackTrace?.ToString() ?? string.Empty);
            var inner = ex.InnerException;

            while (inner is not null) {
                _logger.LogError("============ Inner exception ============");
                _logger.LogError(inner.Message.ToString());
                _logger.LogError(inner.StackTrace?.ToString() ?? string.Empty);
                inner = inner.InnerException;
            }
#if DEBUG
            throw; // so debug tools go straight to the source of the exception when attached
#else
                return 1;
#endif
        }

    }

    private ProxyConfiguration Configuration { get => ConfigurationFactory.Value; }

    private readonly Lazy<ProxyConfiguration> ConfigurationFactory = new(() => {
        var builder = new ConfigurationBuilder();
        var configuration = builder
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
        var configObject = new ProxyConfiguration();
        configuration.Bind(configObject);

        return configObject;
    });
}
