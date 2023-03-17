// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.CommandLine;
using System.Text.RegularExpressions;

namespace Microsoft.Graph.DeveloperProxy;

internal class ProxyHost {
    private Option<int?> _portOption;
    private Option<LogLevel?> _logLevelOption;
    private Option<bool?> _recordOption;
    private static Option<string?>? _configFileOption;

    private static bool _configFileResolved = false;
    private static string _configFile = "appsettings.json";
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
                    var filePath = input.Tokens.First().Value;
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
    }

    public RootCommand GetRootCommand() {
        var command = new RootCommand {
            _portOption,
            _logLevelOption,
            _recordOption,
            // _configFileOption is set during the call to load
            // `ProxyCommandHandler.Configuration`. As such, it's always set here
            _configFileOption!
        };
        command.Description = "Microsoft Graph Developer Proxy is a command line tool that simulates real world behaviors of Microsoft Graph and other APIs, locally.";

        return command;
    }

    public ProxyCommandHandler GetCommandHandler(PluginEvents pluginEvents, ISet<Regex> urlsToWatch, ILogger logger) => new ProxyCommandHandler(_portOption, _logLevelOption, _recordOption, pluginEvents, urlsToWatch, logger);
}

