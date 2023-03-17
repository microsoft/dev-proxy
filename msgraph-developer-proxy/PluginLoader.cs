// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Microsoft.Graph.DeveloperProxy;

internal class PluginLoaderResult {
    public ISet<Regex> UrlsToWatch { get; }
    public IEnumerable<IProxyPlugin> ProxyPlugins { get; }
    public PluginLoaderResult(ISet<Regex> urlsToWatch, IEnumerable<IProxyPlugin> proxyPlugins) {
        UrlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
        ProxyPlugins = proxyPlugins ?? throw new ArgumentNullException(nameof(proxyPlugins));
    }
}

internal class PluginLoader {
    public PluginLoader(ILogger logger) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PluginLoaderResult LoadPlugins(IPluginEvents pluginEvents, IProxyContext proxyContext) {
        List<IProxyPlugin> plugins = new();
        PluginConfig config = PluginConfig;
        List<Regex> globallyWatchedUrls = PluginConfig.UrlsToWatch.Select(ConvertToRegex).ToList();
        ISet<Regex> defaultUrlsToWatch = globallyWatchedUrls.ToHashSet();
        string? rootDirectory = Path.GetDirectoryName(AppContext.BaseDirectory);
        if (!string.IsNullOrEmpty(rootDirectory)) {
            foreach (PluginReference h in config.Plugins) {
                if (!h.Enabled) continue;
                // Load Handler Assembly if enabled
                string pluginLocation = Path.GetFullPath(Path.Combine(rootDirectory, h.PluginPath.Replace('\\', Path.DirectorySeparatorChar)));
                PluginLoadContext pluginLoadContext = new PluginLoadContext(pluginLocation);
                _logger.LogDebug($"Loading from: {pluginLocation}");
                Assembly assembly = pluginLoadContext.LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(pluginLocation)));
                IEnumerable<Regex>? pluginUrlsList = h.UrlsToWatch?.Select(ConvertToRegex);
                ISet<Regex>? pluginUrls = null;
                if (pluginUrlsList is not null) {
                    pluginUrls = pluginUrlsList.ToHashSet();
                    globallyWatchedUrls.AddRange(pluginUrlsList);
                }
                // Load Plugins from assembly
                IProxyPlugin plugin = CreatePlugin(assembly, h);
                plugin.Register(pluginEvents, proxyContext, pluginUrls ?? defaultUrlsToWatch, h.ConfigSection is null ? null : Configuration.GetSection(h.ConfigSection));
                plugins.Add(plugin);
            }
        }
        return plugins.Count > 0
            ? new PluginLoaderResult(globallyWatchedUrls.ToHashSet(), plugins)
            : throw new InvalidDataException("No plugins were loaded");
    }

    private IProxyPlugin CreatePlugin(Assembly assembly, PluginReference h) {
        foreach (Type type in assembly.GetTypes()) {
            if (typeof(IProxyPlugin).IsAssignableFrom(type)) {
                IProxyPlugin? result = Activator.CreateInstance(type) as IProxyPlugin;
                if (result is not null && result.Name == h.Name) {
                    return result;
                }
            }
        }

        string availableTypes = string.Join(",", assembly.GetTypes().Select(t => t.FullName));
        throw new ApplicationException(
            $"Can't find plugin {h.Name} which implements IProxyPlugin in {assembly} from {AppContext.BaseDirectory}.\n" +
            $"Available types: {availableTypes}");
    }

    public static Regex ConvertToRegex(string stringMatcher) =>
        new Regex(Regex.Escape(stringMatcher).Replace("\\*", ".*"), RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private PluginConfig? _pluginConfig;
    private ILogger _logger;

    private PluginConfig PluginConfig {
        get {
            if (_pluginConfig == null) {
                _pluginConfig = new PluginConfig();
                Configuration.Bind(_pluginConfig);
            }
            if (_pluginConfig == null || !_pluginConfig.Plugins.Any()) {
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

internal class PluginConfig {
    public List<PluginReference> Plugins { get; set; } = new();
    public List<string> UrlsToWatch { get; set; } = new();
}

internal class PluginReference {
    public bool Enabled { get; set; } = true;
    public string? ConfigSection { get; set; }
    public string PluginPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string>? UrlsToWatch { get; set; }
}
