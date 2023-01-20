// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.CommandLine;
using System.Text.RegularExpressions;

namespace Microsoft.Graph.DeveloperProxy;

internal class ProxyHost {
    private Option<int> _portOption;
    private Option<LogLevel> _logLevelOption;

    public ProxyHost() {
        _portOption = new Option<int>("--port", "The port for the proxy server to listen on");
        _portOption.AddAlias("-p");
        _portOption.ArgumentHelpName = "port";
        _portOption.SetDefaultValue(8000);

        _logLevelOption = new Option<LogLevel>("--logLevel", "Level of messages to log");
        _logLevelOption.ArgumentHelpName = "logLevel";
        _logLevelOption.SetDefaultValue(LogLevel.Info);
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

