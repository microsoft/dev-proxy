// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.CommandLine;
using System.Text.RegularExpressions;

namespace Microsoft.Graph.DeveloperProxy;

internal class ProxyHost {
    private Option<int> _portOption;

    public ProxyHost() {
        _portOption = new Option<int>("--port", "The port for the proxy server to listen on");
        _portOption.AddAlias("-p");
        _portOption.ArgumentHelpName = "port";
        _portOption.SetDefaultValue(8000);
    }

    public RootCommand GetRootCommand() {
        var command = new RootCommand
        {
                _portOption
            };
        command.Description = "HTTP proxy to create random failures for calls to Microsoft Graph";

        return command;
    }

    public ProxyCommandHandler GetCommandHandler(PluginEvents pluginEvents, ISet<Regex> urlsToWatch, ILogger logger) => new ProxyCommandHandler(_portOption, pluginEvents, urlsToWatch, logger);
}

