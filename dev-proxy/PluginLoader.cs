// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License

using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Microsoft.DevProxy;

internal class PluginLoaderResult
{
    public ISet<UrlToWatch> UrlsToWatch { get; }
    public IEnumerable<IProxyPlugin> ProxyPlugins { get; }
    public PluginLoaderResult(ISet<UrlToWatch> urlsToWatch, IEnumerable<IProxyPlugin> proxyPlugins)
    {
        UrlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
        ProxyPlugins = proxyPlugins ?? throw new ArgumentNullException(nameof(proxyPlugins));
    }
}

internal class PluginLoader
{
    private PluginConfig? _pluginConfig;
    private ILogger _logger;

    public PluginLoader(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PluginLoaderResult LoadPlugins(IPluginEvents pluginEvents, IProxyContext proxyContext)
    {
        List<IProxyPlugin> plugins = new();
        var config = PluginConfig;
        var globallyWatchedUrls = PluginConfig.UrlsToWatch.Select(ConvertToRegex).ToList();
        var defaultUrlsToWatch = globallyWatchedUrls.ToHashSet();
        var configFileDirectory = Path.GetDirectoryName(Path.GetFullPath(ProxyUtils.ReplacePathTokens(ProxyHost.ConfigFile)));
        // key = location
        var pluginContexts = new Dictionary<string, PluginLoadContext>();

        if (!string.IsNullOrEmpty(configFileDirectory))
        {
            foreach (PluginReference h in config.Plugins)
            {
                if (!h.Enabled) continue;
                // Load Handler Assembly if enabled
                var pluginLocation = Path.GetFullPath(Path.Combine(configFileDirectory, ProxyUtils.ReplacePathTokens(h.PluginPath.Replace('\\', Path.DirectorySeparatorChar))));

                if (!pluginContexts.TryGetValue(pluginLocation, out PluginLoadContext? pluginLoadContext))
                {
                    pluginLoadContext = new PluginLoadContext(pluginLocation);
                    pluginContexts.Add(pluginLocation, pluginLoadContext);
                }

                _logger?.LogDebug("Loading plugin {pluginName} from: {pluginLocation}", h.Name, pluginLocation);
                var assembly = pluginLoadContext.LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(pluginLocation)));
                var pluginUrlsList = h.UrlsToWatch?.Select(ConvertToRegex);
                ISet<UrlToWatch>? pluginUrls = null;

                if (pluginUrlsList is not null)
                {
                    pluginUrls = pluginUrlsList.ToHashSet();
                    globallyWatchedUrls.AddRange(pluginUrlsList);
                }

                var plugin = CreatePlugin(
                    assembly,
                    h,
                    pluginEvents,
                    proxyContext,
                    (pluginUrls != null && pluginUrls.Any()) ? pluginUrls : defaultUrlsToWatch,
                    h.ConfigSection is null ? null : Configuration.GetSection(h.ConfigSection)
                );
                _logger?.LogDebug("Registering plugin {pluginName}...", plugin.Name);
                plugin.Register();
                _logger?.LogDebug("Plugin {pluginName} registered.", plugin.Name);
                plugins.Add(plugin);
            }
        }

        return plugins.Count > 0
            ? new PluginLoaderResult(globallyWatchedUrls.ToHashSet(), plugins)
            : throw new InvalidDataException("No plugins were loaded");
    }

    private IProxyPlugin CreatePlugin(
        Assembly assembly,
        PluginReference pluginReference,
        IPluginEvents pluginEvents,
        IProxyContext context,
        ISet<UrlToWatch> urlsToWatch,
        IConfigurationSection? configSection = null
    )
    {
        foreach (Type type in assembly.GetTypes())
        {
            if (type.Name == pluginReference.Name &&
                typeof(IProxyPlugin).IsAssignableFrom(type))
            {
                IProxyPlugin? result = Activator.CreateInstance(type, [pluginEvents, context, _logger, urlsToWatch, configSection]) as IProxyPlugin;
                if (result is not null && result.Name == pluginReference.Name)
                {
                    return result;
                }
            }
        }

        string availableTypes = string.Join(",", assembly.GetTypes().Select(t => t.FullName));
        throw new ApplicationException(
            $"Can't find plugin {pluginReference.Name} which implements IProxyPlugin in {assembly} from {AppContext.BaseDirectory}.\r\n" +
            $"Available types: {availableTypes}");
    }

    public static UrlToWatch ConvertToRegex(string stringMatcher)
    {
        var exclude = false;
        if (stringMatcher.StartsWith("!"))
        {
            exclude = true;
            stringMatcher = stringMatcher.Substring(1);
        }

        return new UrlToWatch(
            new Regex($"^{Regex.Escape(stringMatcher).Replace("\\*", ".*")}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            exclude
        );
    }

    private PluginConfig PluginConfig
    {
        get
        {
            if (_pluginConfig == null)
            {
                _pluginConfig = new PluginConfig();
                Configuration.Bind(_pluginConfig);

                if (ProxyHost.UrlsToWatch is not null && ProxyHost.UrlsToWatch.Any())
                {
                    _pluginConfig.UrlsToWatch = ProxyHost.UrlsToWatch.ToList();
                }
            }
            if (_pluginConfig == null || !_pluginConfig.Plugins.Any())
            {
                throw new InvalidDataException("The configuration must contain at least one plugin");
            }
            return _pluginConfig;
        }
    }

    private IConfigurationRoot Configuration { get => ConfigurationFactory.Value; }

    private readonly Lazy<IConfigurationRoot> ConfigurationFactory = new(() =>
        new ConfigurationBuilder()
                .AddJsonFile(ProxyHost.ConfigFile, optional: true, reloadOnChange: true)
                .Build()
    );
}

internal class PluginConfig
{
    public List<PluginReference> Plugins { get; set; } = new();
    public List<string> UrlsToWatch { get; set; } = new();
}

internal class PluginReference
{
    public bool Enabled { get; set; } = true;
    public string? ConfigSection { get; set; }
    public string PluginPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string>? UrlsToWatch { get; set; }
}
