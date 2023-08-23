// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft365.DeveloperProxy.Abstractions;
using System.CommandLine;

namespace Microsoft365.DeveloperProxy;

internal class ProxyHost {
    private Option<int?> _portOption;
    private Option<LogLevel?> _logLevelOption;
    private Option<bool?> _recordOption;
    private Option<IEnumerable<int>?> _watchPidsOption;
    private Option<IEnumerable<string>?> _watchProcessNamesOption;
    private static Option<string?>? _configFileOption;
    private Option<int?> _rateOption;

    private static bool _configFileResolved = false;
    private static string _configFile = "m365proxyrc.json";
    public static string ConfigFile {
        get {
            if (_configFileResolved) {
                return _configFile;
            }
            
            if (_configFileOption is null) {
                _configFileOption = new Option<string?>("--config-file", "The path to the configuration file");
                _configFileOption.AddAlias("-c");
                _configFileOption.ArgumentHelpName = "configFile";
                _configFileOption.AddValidator(input => {
                    var filePath = ProxyUtils.ReplacePathTokens(input.Tokens.First().Value);
                    if (String.IsNullOrEmpty(filePath)) {
                        return;
                    }

                    if (!File.Exists(filePath)) {
                        input.ErrorMessage = $"File {filePath} does not exist";
                    }
                });
            }

            var result = _configFileOption.Parse(Environment.GetCommandLineArgs());
            var configFile = result.GetValueForOption<string?>(_configFileOption);
            if (configFile is not null) {
                _configFile = configFile;
            }
            else {
                // if there's no config file in the current working folder
                // fall back to the default config file in the app folder
                if (!File.Exists(_configFile)) {
                    _configFile = "~appFolder/m365proxyrc.json";
                }
            }

            _configFile = Path.GetFullPath(ProxyUtils.ReplacePathTokens(_configFile));

            _configFileResolved = true;

            return _configFile;
        }
    }

    public ProxyHost() {
        _portOption = new Option<int?>("--port", "The port for the proxy server to listen on");
        _portOption.AddAlias("-p");
        _portOption.ArgumentHelpName = "port";
        
        _logLevelOption = new Option<LogLevel?>("--log-level", $"Level of messages to log. Allowed values: {string.Join(", ", Enum.GetNames(typeof(LogLevel)))}");
        _logLevelOption.ArgumentHelpName = "logLevel";
        _logLevelOption.AddValidator(input => {
            if (!Enum.TryParse<LogLevel>(input.Tokens.First().Value, true, out var logLevel)) {
                input.ErrorMessage = $"{input.Tokens.First().Value} is not a valid log level. Allowed values are: {string.Join(", ", Enum.GetNames(typeof(LogLevel)))}";
            }
        });

        _recordOption = new Option<bool?>("--record", "Use this option to record all request logs");

        _watchPidsOption = new Option<IEnumerable<int>?>("--watch-pids", "The IDs of processes to watch for requests");
        _watchPidsOption.ArgumentHelpName = "pids";
        _watchPidsOption.AllowMultipleArgumentsPerToken = true;

        _watchProcessNamesOption = new Option<IEnumerable<string>?>("--watch-process-names", "The names of processes to watch for requests");
        _watchProcessNamesOption.ArgumentHelpName = "processNames";
        _watchProcessNamesOption.AllowMultipleArgumentsPerToken = true;

        _rateOption = new Option<int?>("--failure-rate", "The percentage of chance that a request will fail");
        _rateOption.AddAlias("-f");
        _rateOption.ArgumentHelpName = "failure rate";
        _rateOption.AddValidator((input) => {
            int? value = input.GetValueForOption(_rateOption);
            if (value.HasValue && (value < 0 || value > 100)) {
                input.ErrorMessage = $"{value} is not a valid failure rate. Specify a number between 0 and 100";
            }
        });

        ProxyCommandHandler.Configuration.ConfigFile = ConfigFile;
    }

    public RootCommand GetRootCommand(ILogger logger) {
        var command = new RootCommand {
            _portOption,
            _logLevelOption,
            _recordOption,
            _watchPidsOption,
            _watchProcessNamesOption,
            _rateOption,
            // _configFileOption is set during the call to load
            // `ProxyCommandHandler.Configuration`. As such, it's always set here
            _configFileOption!
        };
        command.Description = "Microsoft 365 Developer Proxy is a command line tool for testing Microsoft Graph, SharePoint Online and any other HTTP APIs.";

        var msGraphDbCommand = new Command("msgraphdb", "Generate a local SQLite database with Microsoft Graph API metadata")
        {
            Handler = new MSGraphDbCommandHandler(logger)
        };
        command.Add(msGraphDbCommand);

        return command;
    }

    public ProxyCommandHandler GetCommandHandler(PluginEvents pluginEvents, ISet<UrlToWatch> urlsToWatch, ILogger logger) => new ProxyCommandHandler(_portOption, _logLevelOption, _recordOption, _watchPidsOption, _watchProcessNamesOption, _rateOption, pluginEvents, urlsToWatch, logger);
}

