// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.CommandLine;
using System.Diagnostics;
using System.Net;

var assemblyPath = Process.GetCurrentProcess()?.MainModule?.FileName;
Console.Error.WriteLine($"main module file name {assemblyPath}");
Console.Error.WriteLine($"assembly locaion: {typeof(ProxyEngine).Assembly.Location}");
Console.Error.WriteLine($"app context base directory: {System.AppContext.BaseDirectory}");
var fileVersionInfo = FileVersionInfo.GetVersionInfo(assemblyPath);
Console.Error.WriteLine($"has fileVersionInfo {fileVersionInfo is not null}");
Console.Error.WriteLine($"Product Version: {fileVersionInfo?.ProductVersion}");
Console.Error.WriteLine($"File Version: {fileVersionInfo?.FileVersion}");

PluginEvents pluginEvents = new PluginEvents();
ILogger logger = new ConsoleLogger(ProxyCommandHandler.Configuration, pluginEvents);
IProxyContext context = new ProxyContext(logger);
ProxyHost proxyHost = new();
RootCommand rootCommand = proxyHost.GetRootCommand();
PluginLoaderResult loaderResults = new PluginLoader(logger).LoadPlugins(pluginEvents, context);

// have all the plugins init and provide any command line options
pluginEvents.RaiseInit(new InitArgs(rootCommand));

rootCommand.Handler = proxyHost.GetCommandHandler(pluginEvents, loaderResults.UrlsToWatch, logger);

return await rootCommand.InvokeAsync(args);