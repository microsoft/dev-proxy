using Microsoft.Graph.ChaosProxy;
using System.CommandLine;

return await new ChaosHost().GetRootCommand().InvokeAsync(args);