// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.Text.RegularExpressions;

namespace Microsoft.Graph.DeveloperProxy {
    internal class ProxyHost {
        private Option<int> _portOption;

        public ProxyHost() {
            _portOption = new Option<int>("--port", "The port for the proxy server to listen on");
            _portOption.AddAlias("-p");
            _portOption.ArgumentHelpName = "port";
            _portOption.SetDefaultValue(8000);
        }

        public RootCommand GetRootCommand() {

            //var noMocksOptions = new Option<bool>("--no-mocks", "Disable loading mock requests");
            //noMocksOptions.AddAlias("-n");
            //noMocksOptions.ArgumentHelpName = "no mocks";
            //noMocksOptions.SetDefaultValue(false);

            //var allowedErrorsOption = new Option<IEnumerable<int>>("--allowed-errors", "List of errors that the developer proxy may produce");
            //allowedErrorsOption.AddAlias("-a");
            //allowedErrorsOption.ArgumentHelpName = "allowed errors";
            //allowedErrorsOption.AllowMultipleArgumentsPerToken = true;
            //allowedErrorsOption.SetDefaultValue(new List<int> { 429, 500, 502, 503, 504, 507 });

            var command = new RootCommand
            {
                _portOption
            };
            command.Description = "HTTP proxy to create random failures for calls to Microsoft Graph";

            return command;
        }

        public ProxyCommandHandler GetCommandHandler(PluginEvents pluginEvents, ISet<Regex> urlsToWatch, ILogger logger) => new ProxyCommandHandler(_portOption, pluginEvents, urlsToWatch, logger);
    }
}
