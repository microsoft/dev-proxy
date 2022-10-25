using System.CommandLine;

namespace Microsoft.Graph.DeveloperProxy {
    internal class ProxyHost {
        public RootCommand GetRootCommand() {
            var defaultConfig = new ProxyConfiguration();

            var portOption = new Option<int>("--port", "The port for the proxy server to listen on");
            portOption.AddAlias("-p");
            portOption.ArgumentHelpName = "port";
            portOption.SetDefaultValue(8000);
            
            var rateOption = new Option<int>("--failure-rate", "The percentage of requests to graph to respond with failures");
            rateOption.AddAlias("-f");
            rateOption.ArgumentHelpName = "failure rate";
            rateOption.AddValidator((input) => {
                int value = input.GetValueForOption(rateOption);
                if (value < 0 || value > 100) {
                    input.ErrorMessage = $"{value} is not a valid failure rate. Specify a number between 0 and 100";
                }
            });
            rateOption.SetDefaultValue(50);

            var noMocksOptions = new Option<bool>("--no-mocks", "Disable loading mock requests");
            noMocksOptions.AddAlias("-n");
            noMocksOptions.ArgumentHelpName = "no mocks";
            noMocksOptions.SetDefaultValue(false);

            var cloudOption = new Option<string>("--cloud", "Set the target cloud to proxy requests for");
            cloudOption.AddAlias("-c");
            cloudOption.ArgumentHelpName = "cloud";
            cloudOption.SetDefaultValue("global");

            var allowedErrorsOption = new Option<IEnumerable<int>>("--allowed-errors", "List of errors that the developer proxy may produce");
            allowedErrorsOption.AddAlias("-a");
            allowedErrorsOption.ArgumentHelpName = "allowed errors";
            allowedErrorsOption.AllowMultipleArgumentsPerToken = true;
            allowedErrorsOption.SetDefaultValue(new List<int> { 429, 500, 502, 503, 504, 507 });

            var command = new RootCommand
            {
                portOption,
                rateOption,
                noMocksOptions,
                cloudOption,
                allowedErrorsOption
            };
            command.Description = "HTTP proxy to create random failures for calls to Microsoft Graph";
            command.Handler = new ProxyCommandHandler(portOption, rateOption, noMocksOptions, cloudOption, allowedErrorsOption);
            
            return command;
        }
    }
}
