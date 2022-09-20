using Microsoft.Extensions.Configuration;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Microsoft.Graph.ChaosProxy
{
    public class ChaosProxyCommandHandler : ICommandHandler
    {
        public Option<int> Port { get; set; }
        public Option<int> Rate { get; set; }


        public ChaosProxyCommandHandler(Option<int> port, Option<int> rate)
        {
            Port = port ?? throw new ArgumentNullException(nameof(port));
            Rate = rate ?? throw new ArgumentNullException(nameof(rate));
        }


        public int Invoke(InvocationContext context)
        {
            return InvokeAsync(context).GetAwaiter().GetResult();
        }

        public async Task<int> InvokeAsync(InvocationContext context)
        {
            int port = context.ParseResult.GetValueForOption(Port);
            int failureRate = context.ParseResult.GetValueForOption(Rate);
            CancellationToken cancellationToken = (CancellationToken)context.BindingContext.GetService(typeof(CancellationToken));
            Configuration.Port = port;
            Configuration.FailureRate = failureRate; try
            {
                await new ChaosEngine(Configuration).Run(cancellationToken);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("An error occured while running the Chaos Proxy");
                Console.Error.WriteLine(ex.Message.ToString());
                Console.Error.WriteLine(ex.StackTrace?.ToString());
                var inner = ex.InnerException;
                while (inner is not null)
                {
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
        
        private ChaosProxyConfiguration Configuration { get => ConfigurationFactory.Value; }
        
        private readonly Lazy<ChaosProxyConfiguration> ConfigurationFactory = new(() => {
            var builder = new ConfigurationBuilder();
            var configuration = builder
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .Build();
            var configObject = new ChaosProxyConfiguration();
            configuration.Bind(configObject);
            return configObject;
        });
    }
}
