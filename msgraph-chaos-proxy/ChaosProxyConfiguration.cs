namespace Microsoft.Graph.ChaosProxy
{
    public class ChaosProxyConfiguration
    {
        public int Port { get; set; } = 8000;
        public int FailureRate { get; set; } = 50;
    }
}
