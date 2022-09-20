// See https://aka.ms/new-console-template for more information
using Microsoft.Graph.ChaosProxy;
using System.CommandLine;

return await new ChaosHost().GetRootCommand().InvokeAsync(args);