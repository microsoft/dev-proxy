// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft365.DeveloperProxy;
using Microsoft365.DeveloperProxy.Abstractions;
using System.CommandLine;

PluginEvents pluginEvents = new PluginEvents();
ILogger logger = new ConsoleLogger(ProxyCommandHandler.Configuration, pluginEvents);
IProxyContext context = new ProxyContext(logger, ProxyCommandHandler.Configuration);
ProxyHost proxyHost = new();
RootCommand rootCommand = proxyHost.GetRootCommand();
PluginLoaderResult loaderResults = new PluginLoader(logger).LoadPlugins(pluginEvents, context);

// have all the plugins init and provide any command line options
pluginEvents.RaiseInit(new InitArgs(rootCommand));

rootCommand.Handler = proxyHost.GetCommandHandler(pluginEvents, loaderResults.UrlsToWatch, logger);

// store the global options that are created automatically for us
string[] globalOptions = { "--version", "-?", "-h", "--help" };

// filter args to retrieve short (-n) and long (--name) option aliases
var incomingOptions = args.Where(arg => arg.StartsWith("-") || arg.StartsWith("--")).ToArray();

// remove the global options from the incoming options
incomingOptions = incomingOptions.Except(globalOptions).ToArray();

// compare the incoming options against the root command options
foreach (var option in rootCommand.Options)
{
    // get the option aliases
    var aliases = option.Aliases.ToArray();

    // iterate over aliases
    foreach (string alias in aliases)
    {
        // if the alias is present
        if (incomingOptions.Contains(alias))
        {
            // remove the option from the incoming options
            incomingOptions = incomingOptions.Where(val => val != alias).ToArray();
        }
    }
}

// list the remaining incoming options as unknown in the output
if (incomingOptions.Length > 0)
{
    logger.LogInfo($"Unknown option(s): {string.Join(" ", incomingOptions)}");
    logger.LogInfo($"TIP: Use -?, -h or --help to view available options");
    logger.LogInfo($"TIP: Are you missing a plugin? See: https://aka.ms/m365/proxy/plugins");
}
else
{
    await rootCommand.InvokeAsync(args);
}
