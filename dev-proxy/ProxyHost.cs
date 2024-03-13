// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Net;

namespace Microsoft.DevProxy;

internal class ProxyHost
{
    internal static readonly string PortOptionName = "--port";
    private Option<int?> _portOption;
    internal static readonly string IpAddressOptionName = "--ip-address";
    private Option<string?> _ipAddressOption;
    internal static readonly string LogLevelOptionName = "--log-level";
    private static Option<LogLevel?>? _logLevelOption;
    internal static readonly string RecordOptionName = "--record";
    private Option<bool?> _recordOption;
    internal static readonly string WatchPidsOptionName = "--watch-pids";
    private Option<IEnumerable<int>?> _watchPidsOption;
    internal static readonly string WatchProcessNamesOptionName = "--watch-process-names";
    private Option<IEnumerable<string>?> _watchProcessNamesOption;
    internal static readonly string ConfigFileOptionName = "--config-file";
    private static Option<string?>? _configFileOption;
    internal static readonly string RateOptionName = "--failure-rate";
    private Option<int?> _rateOption;
    internal static readonly string NoFirstRunOptionName = "--no-first-run";
    private Option<bool?> _noFirstRunOption;
    internal static readonly string AsSystemProxyOptionName = "--as-system-proxy";
    private Option<bool?> _asSystemProxyOption;
    internal static readonly string InstallCertOptionName = "--install-cert";
    private Option<bool?> _installCertOption;
    internal static readonly string UrlsToWatchOptionName = "--urls-to-watch";
    private static Option<IEnumerable<string>?>? _urlsToWatchOption;

    private static bool _configFileResolved = false;
    private static string _configFile = "devproxyrc.json";
    public static string ConfigFile
    {
        get
        {
            if (_configFileResolved)
            {
                return _configFile;
            }

            if (_configFileOption is null)
            {
                _configFileOption = new Option<string?>("--config-file", "The path to the configuration file");
                _configFileOption.AddAlias("-c");
                _configFileOption.ArgumentHelpName = "configFile";
                _configFileOption.AddValidator(input =>
                {
                    var filePath = ProxyUtils.ReplacePathTokens(input.Tokens.First().Value);
                    if (string.IsNullOrEmpty(filePath))
                    {
                        return;
                    }

                    if (!File.Exists(filePath))
                    {
                        input.ErrorMessage = $"Configuration file {filePath} does not exist";
                    }
                });
            }

            var result = _configFileOption.Parse(Environment.GetCommandLineArgs());
            // since we're parsing all args, and other options are not instantiated yet
            // we're getting here a bunch of other errors, so we only need to look for
            // errors related to the config file option
            var error = result.Errors.Where(e => e.SymbolResult?.Symbol == _configFileOption).FirstOrDefault();
            if (error is not null)
            {
                // Logger is not available here yet so we need to fallback to Console
                var color = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(error.Message);
                Console.ForegroundColor = color;
                Environment.Exit(1);
            }

            var configFile = result.GetValueForOption(_configFileOption);
            if (configFile is not null)
            {
                _configFile = configFile;
            }
            else
            {
                // if there's no config file in the current working folder
                // fall back to the default config file in the app folder
                if (!File.Exists(_configFile) && !File.Exists("devproxyrc.jsonc"))
                {
                    _configFile = "~appFolder/devproxyrc.json";
                }
            }

            _configFile = Path.GetFullPath(ProxyUtils.ReplacePathTokens(_configFile));

            _configFileResolved = true;

            return _configFile;
        }
    }

    private static bool _logLevelResolved = false;
    private static LogLevel? _logLevel;
    public static LogLevel? LogLevel
    {
        get
        {
            if (_logLevelResolved)
            {
                return _logLevel;
            }

            if (_logLevelOption is null)
            {
                _logLevelOption = new Option<LogLevel?>(
                    "--log-level",
                    $"Level of messages to log. Allowed values: {string.Join(", ", Enum.GetNames(typeof(LogLevel)))}"
                )
                {
                    ArgumentHelpName = "logLevel"
                };
                _logLevelOption.AddValidator(input =>
                {
                    if (!Enum.TryParse<LogLevel>(input.Tokens.First().Value, true, out var logLevel))
                    {
                        input.ErrorMessage = $"{input.Tokens.First().Value} is not a valid log level. Allowed values are: {string.Join(", ", Enum.GetNames(typeof(LogLevel)))}";
                    }
                });
            }

            var result = _logLevelOption.Parse(Environment.GetCommandLineArgs());
            // since we're parsing all args, and other options are not instantiated yet
            // we're getting here a bunch of other errors, so we only need to look for
            // errors related to the log level option
            var error = result.Errors.Where(e => e.SymbolResult?.Symbol == _logLevelOption).FirstOrDefault();
            if (error is not null)
            {
                // Logger is not available here yet so we need to fallback to Console
                var color = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(error.Message);
                Console.ForegroundColor = color;
                Environment.Exit(1);
            }

            _logLevel = result.GetValueForOption(_logLevelOption);
            _logLevelResolved = true;

            return _logLevel;
        }
    }

    private static bool _urlsToWatchResolved = false;
    private static IEnumerable<string>? _urlsToWatch;
    public static IEnumerable<string>? UrlsToWatch
    {
        get
        {
            if (_urlsToWatchResolved)
            {
                return _urlsToWatch;
            }

            var result = _urlsToWatchOption!.Parse(Environment.GetCommandLineArgs());
            // since we're parsing all args, and other options are not instantiated yet
            // we're getting here a bunch of other errors, so we only need to look for
            // errors related to the log level option
            var error = result.Errors.Where(e => e.SymbolResult?.Symbol == _urlsToWatchOption).FirstOrDefault();
            if (error is not null)
            {
                // Logger is not available here yet so we need to fallback to Console
                var color = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(error.Message);
                Console.ForegroundColor = color;
                Environment.Exit(1);
            }

            _urlsToWatch = result.GetValueForOption(_urlsToWatchOption!);
            _urlsToWatchResolved = true;

            return _urlsToWatch;
        }
    }

    public ProxyHost()
    {
        _portOption = new Option<int?>(PortOptionName, "The port for the proxy to listen on");
        _portOption.AddAlias("-p");
        _portOption.ArgumentHelpName = "port";

        _ipAddressOption = new Option<string?>(IpAddressOptionName, "The IP address for the proxy to bind to")
        {
            ArgumentHelpName = "ipAddress"
        };
        _ipAddressOption.AddValidator(input =>
        {
            if (!IPAddress.TryParse(input.Tokens.First().Value, out var ipAddress))
            {
                input.ErrorMessage = $"{input.Tokens.First().Value} is not a valid IP address";
            }
        });

        _recordOption = new Option<bool?>(RecordOptionName, "Use this option to record all request logs");

        _watchPidsOption = new Option<IEnumerable<int>?>(WatchPidsOptionName, "The IDs of processes to watch for requests")
        {
            ArgumentHelpName = "pids",
            AllowMultipleArgumentsPerToken = true
        };

        _watchProcessNamesOption = new Option<IEnumerable<string>?>(WatchProcessNamesOptionName, "The names of processes to watch for requests")
        {
            ArgumentHelpName = "processNames",
            AllowMultipleArgumentsPerToken = true
        };

        _rateOption = new Option<int?>(RateOptionName, "The percentage of chance that a request will fail");
        _rateOption.AddAlias("-f");
        _rateOption.ArgumentHelpName = "failure rate";
        _rateOption.AddValidator((input) =>
        {
            int? value = input.GetValueForOption(_rateOption);
            if (value.HasValue && (value < 0 || value > 100))
            {
                input.ErrorMessage = $"{value} is not a valid failure rate. Specify a number between 0 and 100";
            }
        });

        _noFirstRunOption = new Option<bool?>(NoFirstRunOptionName, "Skip the first run experience");

        _asSystemProxyOption = new Option<bool?>(AsSystemProxyOptionName, "Set Dev Proxy as the system proxy");
        _asSystemProxyOption.SetDefaultValue(true);

        _installCertOption = new Option<bool?>(InstallCertOptionName, "Install self-signed certificate");
        _installCertOption.SetDefaultValue(true);
        _installCertOption.AddValidator((input) =>
        {
            var asSystemProxy = input.GetValueForOption(_asSystemProxyOption) ?? true;
            var installCert = input.GetValueForOption(_installCertOption) ?? true;
            if (asSystemProxy && !installCert)
            {
                input.ErrorMessage = $"Requires option '--{_asSystemProxyOption.Name}' to be 'false'";
            }
        });

        _urlsToWatchOption = new(UrlsToWatchOptionName, "The list of URLs to watch for requests")
        {
            ArgumentHelpName = "urlsToWatch",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore
        };
        _urlsToWatchOption.AddAlias("-u");

        ProxyCommandHandler.Configuration.ConfigFile = ConfigFile;
    }

    public RootCommand GetRootCommand(IProxyLogger logger)
    {
        var command = new RootCommand {
            _portOption,
            _ipAddressOption,
            // _logLevelOption is set while initializing the Program
            // As such, it's always set here
            _logLevelOption!,
            _recordOption,
            _watchPidsOption,
            _watchProcessNamesOption,
            _rateOption,
            // _configFileOption is set during the call to load
            // `ProxyCommandHandler.Configuration`. As such, it's always set here
            _configFileOption!,
            _noFirstRunOption,
            _asSystemProxyOption,
            _installCertOption,
            // _urlsToWatchOption is set while initialize the Program
            // As such, it's always set here
            _urlsToWatchOption!
        };
        command.Description = "Dev Proxy is a command line tool for testing Microsoft Graph, SharePoint Online and any other HTTP APIs.";

        var msGraphDbCommand = new Command("msgraphdb", "Generate a local SQLite database with Microsoft Graph API metadata")
        {
            Handler = new MSGraphDbCommandHandler(logger)
        };
        command.Add(msGraphDbCommand);

        var presetCommand = new Command("preset", "Manage Dev Proxy presets");

        var presetGetCommand = new Command("get", "Download the specified preset from the Sample Solution Gallery");
        var presetIdArgument = new Argument<string>("preset-id", "The ID of the preset to download");
        presetGetCommand.AddArgument(presetIdArgument);
        presetGetCommand.SetHandler(async presetId => await PresetGetCommandHandler.DownloadPreset(presetId, logger), presetIdArgument);
        presetCommand.Add(presetGetCommand);

        command.Add(presetCommand);

        return command;
    }

    public ProxyCommandHandler GetCommandHandler(PluginEvents pluginEvents, Option[] optionsFromPlugins, ISet<UrlToWatch> urlsToWatch, IProxyLogger logger) => new ProxyCommandHandler(
        pluginEvents,
        new Option[] {
            _portOption,
            _ipAddressOption,
            _logLevelOption!,
            _recordOption,
            _watchPidsOption,
            _watchProcessNamesOption,
            _rateOption,
            _noFirstRunOption,
            _asSystemProxyOption,
            _installCertOption,
        }.Concat(optionsFromPlugins).ToArray(),
        urlsToWatch,
        logger
    );
}

