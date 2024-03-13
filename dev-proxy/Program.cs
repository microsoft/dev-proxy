// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy;
using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Logging;
using System.CommandLine;

PluginEvents pluginEvents = new PluginEvents();
IProxyLogger logger = new ConsoleLogger(ProxyCommandHandler.Configuration, pluginEvents);
// set the log level if specified through args
if (ProxyHost.LogLevel is not null)
{
    logger.LogLevel = ProxyHost.LogLevel.Value;
}
IProxyContext context = new ProxyContext(logger, ProxyCommandHandler.Configuration, ProxyEngine.Certificate);
ProxyHost proxyHost = new();

// this is where the root command is created which contains all commands and subcommands
RootCommand rootCommand = proxyHost.GetRootCommand(logger);

// store the global options that are created automatically for us
// rootCommand doesn't return the global options, so we have to store them manually
string[] globalOptions = { "--version", "--help", "-h", "/h", "-?", "/?" };

// check if any of the global options are present
var hasGlobalOption = args.Any(arg => globalOptions.Contains(arg));

// get the list of available subcommands
var subCommands = rootCommand.Children.OfType<Command>().Select(c => c.Name).ToArray();

// check if any of the subcommands are present
var hasSubCommand = args.Any(arg => subCommands.Contains(arg));

if (hasGlobalOption || hasSubCommand)
{
    // we don't need to load plugins if the user is using a global option or using a subcommand, so we can exit early
    await rootCommand.InvokeAsync(args);
    return;
}

PluginLoaderResult loaderResults = new PluginLoader(logger).LoadPlugins(pluginEvents, context);
// have all the plugins init
pluginEvents.RaiseInit(new InitArgs());

var options = loaderResults.ProxyPlugins
    .SelectMany(p => p.GetOptions())
    // remove duplicates by comparing the option names
    .GroupBy(o => o.Name)
    .Select(g => g.First())
    .ToList();
options.ForEach(rootCommand.AddOption);
// register all plugin commands
loaderResults.ProxyPlugins
    .SelectMany(p => p.GetCommands())
    .ToList()
    .ForEach(rootCommand.AddCommand);

rootCommand.Handler = proxyHost.GetCommandHandler(pluginEvents, options.ToArray(), loaderResults.UrlsToWatch, logger);

// filter args to retrieve options
var incomingOptions = args.Where(arg => arg.StartsWith("-")).ToArray();

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
    logger.LogError("Unknown option(s): {unknownOptions}", string.Join(" ", incomingOptions));
    logger.LogInformation("TIP: Use --help view available options");
    logger.LogInformation("TIP: Are you missing a plugin? See: https://aka.ms/devproxy/plugins");
}
else
{
    await rootCommand.InvokeAsync(args);
}
