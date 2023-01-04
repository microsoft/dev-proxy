// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.CommandLine;

ILogger logger = new ConsoleLogger();
IProxyContext context = new ProxyContext(logger);
ProxyHost proxyHost = new();
RootCommand rootCommand = proxyHost.GetRootCommand();
PluginEvents pluginEvents = new PluginEvents();
PluginLoaderResult loaderResults = new PluginLoader(logger).LoadPlugins(pluginEvents, context);

// have all the plugins init and provide any command line options
pluginEvents.FireInit(new InitArgs(rootCommand));

rootCommand.Handler = proxyHost.GetCommandHandler(pluginEvents, loaderResults.UrlsToWatch, logger);

return await rootCommand.InvokeAsync(args);