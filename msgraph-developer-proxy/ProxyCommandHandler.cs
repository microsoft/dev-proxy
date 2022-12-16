// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Microsoft.Graph.DeveloperProxy {
    public class ProxyCommandHandler : ICommandHandler {
        public Option<int> Port { get; set; }
        public Option<int> Rate { get; set; }
        public Option<bool> DisableMocks { get; set; }
        public Option<IEnumerable<int>> AllowedErrors { get; }

        public ProxyCommandHandler(Option<int> port, Option<int> rate, Option<bool> disableMocks, Option<IEnumerable<int>> allowedErrors) {
            Port = port ?? throw new ArgumentNullException(nameof(port));
            Rate = rate ?? throw new ArgumentNullException(nameof(rate));
            DisableMocks = disableMocks ?? throw new ArgumentNullException(nameof(disableMocks));
            AllowedErrors = allowedErrors ?? throw new ArgumentNullException(nameof(allowedErrors));
        }

        public int Invoke(InvocationContext context) {
            return InvokeAsync(context).GetAwaiter().GetResult();
        }

        public async Task<int> InvokeAsync(InvocationContext context) {
            int port = context.ParseResult.GetValueForOption(Port);
            int failureRate = context.ParseResult.GetValueForOption(Rate);
            bool disableMocks = context.ParseResult.GetValueForOption(DisableMocks);
            IEnumerable<int> allowedErrors = context.ParseResult.GetValueForOption(AllowedErrors) ?? Enumerable.Empty<int>();
            CancellationToken? cancellationToken = (CancellationToken?)context.BindingContext.GetService(typeof(CancellationToken?));
            Configuration.Port = port;
            Configuration.FailureRate = failureRate;
            Configuration.NoMocks = disableMocks;
            Configuration.AllowedErrors = allowedErrors;

            var newReleaseInfo = await UpdateNotification.CheckForNewVersion();
            if (newReleaseInfo != null) {
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Error.WriteLine($"New version {newReleaseInfo.Version} of the Graph Developer Proxy is available.");
                Console.Error.WriteLine($"See {newReleaseInfo.Url} for more information.");
                Console.Error.WriteLine();
                Console.ForegroundColor = originalColor;
            }

            try {
                await new ChaosEngine(Configuration).Run(cancellationToken);
                return 0;
            }
            catch (Exception ex) {
                Console.Error.WriteLine("An error occurred while running the Developer Proxy");
                Console.Error.WriteLine(ex.Message.ToString());
                Console.Error.WriteLine(ex.StackTrace?.ToString());
                var inner = ex.InnerException;

                while (inner is not null) {
                    Console.Error.WriteLine("============ Inner exception ============");
                    Console.Error.WriteLine(inner.Message.ToString());
                    Console.Error.WriteLine(inner.StackTrace?.ToString());
                    inner = inner.InnerException;
                }
#if DEBUG
                throw; // so debug tools go straight to the source of the exception when attached
#else
                return 1;
#endif
            }

        }

        private ProxyConfiguration Configuration { get => ConfigurationFactory.Value; }

        private readonly Lazy<ProxyConfiguration> ConfigurationFactory = new(() => {
            var builder = new ConfigurationBuilder();
            var configuration = builder
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .Build();
            var configObject = new ProxyConfiguration();
            configuration.Bind(configObject);

            // Read responses separately because ConfigurationBuilder can't properly handle
            // complex JSON objects
            configObject.LoadResponses();

            return configObject;
        });
    }
}
