// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Microsoft.DevProxy;

public class ProxyCommandHandler : ICommandHandler {
    public Option<int?> Port { get; set; }
    public Option<string?> IPAddress { get; set; }
    public Option<LogLevel?> LogLevel { get; set; }
    public Option<bool?> Record { get; set; }
    public Option<IEnumerable<int>?> WatchPids { get; set; }
    public Option<IEnumerable<string>?> WatchProcessNames { get; set; }
    public Option<int?> Rate { get; set; }
    public Option<bool?> NoFirstRun { get; set; }

    private readonly PluginEvents _pluginEvents;
    private readonly ISet<UrlToWatch> _urlsToWatch;
    private readonly ILogger _logger;

    public ProxyCommandHandler(Option<int?> port,
                               Option<string?> ipAddress,
                               Option<LogLevel?> logLevel,
                               Option<bool?> record,
                               Option<IEnumerable<int>?> watchPids,
                               Option<IEnumerable<string>?> watchProcessNames,
                               Option<int?> rate,
                               Option<bool?> noFirstRun,
                               PluginEvents pluginEvents,
                               ISet<UrlToWatch> urlsToWatch,
                               ILogger logger) {
        Port = port ?? throw new ArgumentNullException(nameof(port));
        IPAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
        LogLevel = logLevel ?? throw new ArgumentNullException(nameof(logLevel));
        Record = record ?? throw new ArgumentNullException(nameof(record));
        WatchPids = watchPids ?? throw new ArgumentNullException(nameof(watchPids));
        WatchProcessNames = watchProcessNames ?? throw new ArgumentNullException(nameof(watchProcessNames));
        Rate = rate ?? throw new ArgumentNullException(nameof(rate));
        NoFirstRun = noFirstRun ?? throw new ArgumentNullException(nameof(noFirstRun));
        _pluginEvents = pluginEvents ?? throw new ArgumentNullException(nameof(pluginEvents));
        _urlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Invoke(InvocationContext context) {
        return InvokeAsync(context).GetAwaiter().GetResult();
    }

    public async Task<int> InvokeAsync(InvocationContext context) {
        var port = context.ParseResult.GetValueForOption(Port);
        if (port is not null) {
            Configuration.Port = port.Value;
        }
        var ipAddress = context.ParseResult.GetValueForOption(IPAddress);
        if (ipAddress is not null) {
            Configuration.IPAddress = ipAddress;
        }
        var logLevel = context.ParseResult.GetValueForOption(LogLevel);
        if (logLevel is not null) {
            _logger.LogLevel = logLevel.Value;
        }
        var record = context.ParseResult.GetValueForOption(Record);
        if (record is not null) {
            Configuration.Record = record.Value;
        }
        var watchPids = context.ParseResult.GetValueForOption(WatchPids);
        if (watchPids is not null) {
            Configuration.WatchPids = watchPids;
        }
        var watchProcessNames = context.ParseResult.GetValueForOption(WatchProcessNames);
        if (watchProcessNames is not null) {
            Configuration.WatchProcessNames = watchProcessNames;
        }
        var rate = context.ParseResult.GetValueForOption(Rate);
        if (rate is not null) {
            Configuration.Rate = rate.Value;
        }
        var noFirstRun = context.ParseResult.GetValueForOption(NoFirstRun);
        if (noFirstRun is not null) {
            Configuration.NoFirstRun = noFirstRun.Value;
        }

        CancellationToken? cancellationToken = (CancellationToken?)context.BindingContext.GetService(typeof(CancellationToken?));

        _pluginEvents.RaiseOptionsLoaded(new OptionsLoadedArgs(context));

        var newReleaseInfo = await UpdateNotification.CheckForNewVersion();
        if (newReleaseInfo != null) {
            _logger.LogError($"New Dev Proxy version {newReleaseInfo.Version} is available.");
            _logger.LogError($"See https://aka.ms/devproxy/upgrade for more information.");
            _logger.LogError(string.Empty);
        }

        try {
            await new ProxyEngine(Configuration, _urlsToWatch, _pluginEvents, _logger).Run(cancellationToken);
            return 0;
        }
        catch (Exception ex) {
            _logger.LogError("An error occurred while running Dev Proxy");
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

    public static ProxyConfiguration Configuration { get => ConfigurationFactory.Value; }

    private static readonly Lazy<ProxyConfiguration> ConfigurationFactory = new(() => {
        var builder = new ConfigurationBuilder();
        var configuration = builder
                .AddJsonFile(ProxyHost.ConfigFile, optional: true, reloadOnChange: true)
                .Build();
        var configObject = new ProxyConfiguration();
        configuration.Bind(configObject);

        return configObject;
    });
}
