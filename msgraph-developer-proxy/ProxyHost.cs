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
            _recordOption
        };
        command.Description = "Microsoft Graph Developer Proxy is a command line tool that simulates real world behaviors of Microsoft Graph and other APIs, locally.";

        return command;
    }

    public ProxyCommandHandler GetCommandHandler(PluginEvents pluginEvents, ISet<Regex> urlsToWatch, ILogger logger) => new ProxyCommandHandler(_portOption, _logLevelOption, _recordOption, pluginEvents, urlsToWatch, logger);
}

