// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.CommandLine;
using System.Text.RegularExpressions;

namespace Microsoft.Graph.DeveloperProxy;

internal class ProxyHost {
    private Option<int?> _portOption;
    private Option<LogLevel?> _logLevelOption;

    public ProxyHost() {
        _portOption = new Option<int?>("--port", "The port for the proxy server to listen on");
        _portOption.AddAlias("-p");
        _portOption.ArgumentHelpName = "port";

        _logLevelOption = new Option<LogLevel?>("--logLevel", $"Level of messages to log. Allowed values: {string.Join(", ", Enum.GetNames(typeof(LogLevel)))}");
        _logLevelOption.ArgumentHelpName = "logLevel";
        _logLevelOption.AddValidator(input => {
            if (!Enum.TryParse<LogLevel>(input.Tokens.First().Value, true, out var logLevel)) {
                input.ErrorMessage = $"{input.Tokens.First().Value} is not a valid log level. Allowed values are: {string.Join(", ", Enum.GetNames(typeof(LogLevel)))}";
            }
        });
    }

    public RootCommand GetRootCommand() {
        var command = new RootCommand {
            _portOption,
            _logLevelOption
        };
        command.Description = "HTTP proxy to create random failures for calls to Microsoft Graph";

        return command;
    }

    public ProxyCommandHandler GetCommandHandler(PluginEvents pluginEvents, ISet<Regex> urlsToWatch, ILogger logger) => new ProxyCommandHandler(_portOption, _logLevelOption, pluginEvents, urlsToWatch, logger);
}

