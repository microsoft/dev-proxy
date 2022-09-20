using System.CommandLine;

namespace Microsoft.Graph.ChaosProxy {
    internal class ChaosHost {
        public RootCommand GetRootCommand() {
            var defaultConfig = new ChaosProxyConfiguration();

            var portOption = new Option<int>("--port", "The port for the proxy server to listen on");
            portOption.AddAlias("-p");
            portOption.ArgumentHelpName = "port";
            
            var rateOption = new Option<int>("--failure-rate", "The percentage of requests to graph to respond with failures");
            rateOption.AddAlias("-f");
            rateOption.ArgumentHelpName = "failure rate";
            rateOption.AddValidator((input) => {
                int value = input.GetValueForOption(rateOption);
                if (value < 0 || value > 100) {
                    input.ErrorMessage = $"{value} is not a valid failure rate. Specify a number between 0 and 100";
                }
            });

            var noMocksOptions = new Option<bool>("--no-mocks", "Disable loading mock requests");
            noMocksOptions.ArgumentHelpName = "no mocks";
            
            var command = new RootCommand
            {
                portOption,
                rateOption,
                noMocksOptions
            };
            command.Description = "HTTP proxy to create random failures for calls to Microsoft Graph";
            command.Handler = new ChaosProxyCommandHandler(portOption, rateOption, noMocksOptions);
            
            return command;
        }
    }
}
