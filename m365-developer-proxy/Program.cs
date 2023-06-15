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

return await rootCommand.InvokeAsync(args);