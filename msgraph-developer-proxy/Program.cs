// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy;
using System.CommandLine;

ILogger logger = new ConsoleLogger();
IProxyContext context = new ProxyContext(logger);
ProxyHost proxyHost = new();
RootCommand rootCommand = proxyHost.GetRootCommand();
PluginEvents pluginEvents = new PluginEvents();
PluginLoaderResult loaderResults = new PluginLoader().LoadPlugins(pluginEvents, context);

// have all the plugins init and provide any command line options
pluginEvents.FireInit(new InitArgs(rootCommand));

rootCommand.Handler = proxyHost.GetCommandHandler(pluginEvents, loaderResults.UrlsToWatch, logger);

return await rootCommand.InvokeAsync(args);