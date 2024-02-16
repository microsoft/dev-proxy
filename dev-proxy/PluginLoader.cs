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
    public PluginLoader(IProxyLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PluginLoaderResult LoadPlugins(IPluginEvents pluginEvents, IProxyContext proxyContext)
    {
        List<IProxyPlugin> plugins = new();
        PluginConfig config = PluginConfig;
        List<UrlToWatch> globallyWatchedUrls = PluginConfig.UrlsToWatch.Select(ConvertToRegex).ToList();
        ISet<UrlToWatch> defaultUrlsToWatch = globallyWatchedUrls.ToHashSet();
        string? configFileDirectory = Path.GetDirectoryName(Path.GetFullPath(ProxyUtils.ReplacePathTokens(ProxyHost.ConfigFile)));
        if (!string.IsNullOrEmpty(configFileDirectory))
        {
            foreach (PluginReference h in config.Plugins)
            {
                if (!h.Enabled) continue;
                // Load Handler Assembly if enabled
                string pluginLocation = Path.GetFullPath(Path.Combine(configFileDirectory, ProxyUtils.ReplacePathTokens(h.PluginPath.Replace('\\', Path.DirectorySeparatorChar))));
                PluginLoadContext pluginLoadContext = new PluginLoadContext(pluginLocation);
                _logger.LogDebug("Loading plugin {pluginName} from: {pluginLocation}", h.Name, pluginLocation);
                Assembly assembly = pluginLoadContext.LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(pluginLocation)));
                IEnumerable<UrlToWatch>? pluginUrlsList = h.UrlsToWatch?.Select(ConvertToRegex);
                ISet<UrlToWatch>? pluginUrls = null;
                if (pluginUrlsList is not null)
                {
                    pluginUrls = pluginUrlsList.ToHashSet();
                    globallyWatchedUrls.AddRange(pluginUrlsList);
                }
                // Load Plugins from assembly
                IProxyPlugin plugin = CreatePlugin(assembly, h);
                plugin.Register(
                    pluginEvents,
                    proxyContext,
                    (pluginUrls != null && pluginUrls.Any()) ? pluginUrls : defaultUrlsToWatch,
                    h.ConfigSection is null ? null : Configuration.GetSection(h.ConfigSection)
                );
                plugins.Add(plugin);
            }
        }

        return plugins.Count > 0
            ? new PluginLoaderResult(globallyWatchedUrls.ToHashSet(), plugins)
            : throw new InvalidDataException("No plugins were loaded");
    }

    private IProxyPlugin CreatePlugin(Assembly assembly, PluginReference h)
    {
        foreach (Type type in assembly.GetTypes())
        {
            if (typeof(IProxyPlugin).IsAssignableFrom(type))
            {
                IProxyPlugin? result = Activator.CreateInstance(type) as IProxyPlugin;
                if (result is not null && result.Name == h.Name)
                {
                    return result;
                }
            }
        }

        string availableTypes = string.Join(",", assembly.GetTypes().Select(t => t.FullName));
        throw new ApplicationException(
            $"Can't find plugin {h.Name} which implements IProxyPlugin in {assembly} from {AppContext.BaseDirectory}.\r\n" +
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

    private PluginConfig? _pluginConfig;
    private IProxyLogger _logger;

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
