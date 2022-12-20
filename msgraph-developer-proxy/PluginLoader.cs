// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Microsoft.Graph.DeveloperProxy {

    internal class PluginLoaderResult {
        public ISet<Regex> UrlsToWatch { get; }
        public IEnumerable<IProxyPlugin> ProxyPlugins { get; }
        public PluginLoaderResult(ISet<Regex> urlsToWatch, IEnumerable<IProxyPlugin> proxyPlugins) {
            UrlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
            ProxyPlugins = proxyPlugins ?? throw new ArgumentNullException(nameof(proxyPlugins));
        }
    }

    internal class PluginLoader {
        public PluginLoader() {
        }
        public PluginLoaderResult LoadPlugins(IPluginEvents pluginEvents, IProxyContext proxyContext) {
            
            List<IProxyPlugin> plugins = new();
            HandlerConfig config = HandlerConfig;
            List<Regex> allWatchedUrls = HandlerConfig.UrlsToWatch.Select(ConvertToRegex).ToList();
            ISet<Regex> urlsToWatch = allWatchedUrls.ToHashSet();
            foreach (HandlerReference h in config.Handlers) {
                // Load Handler Assembly
                string? root = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                if (!string.IsNullOrEmpty(root)) {
                    string pluginLocation = Path.GetFullPath(Path.Combine(root, h.HandlerPath.Replace('\\', Path.DirectorySeparatorChar)));
                    PluginLoadContext pluginLoadContext = new PluginLoadContext(pluginLocation);
                    Console.WriteLine($"Loading from: {pluginLocation}");
                    Assembly assembly = pluginLoadContext.LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(pluginLocation)));
                    IEnumerable<Regex>? handlerUrlsList = h.UrlsToWatch?.Select(ConvertToRegex);
                    ISet<Regex>? handlerUrls = null;
                    if (handlerUrlsList is not null) {
                        handlerUrls = handlerUrlsList.ToHashSet();
                        allWatchedUrls.AddRange(handlerUrlsList);
                    }
                    // Load Plugins from assembly
                    IProxyPlugin plugin = CreateHandler(assembly, h);
                    plugin.Register(pluginEvents, proxyContext, handlerUrls ?? urlsToWatch, h.ConfigSection is null ? null : Configuration.GetSection(h.ConfigSection));
                    plugins.Add(plugin);
                }
            }
            return plugins.Count > 0
                ? new PluginLoaderResult(allWatchedUrls.ToHashSet(), plugins)
                : throw new InvalidDataException("No handlers were loaded");
        }

        private IProxyPlugin CreateHandler(Assembly assembly, HandlerReference h) {

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
                $"Can't find plugin {h.Name} which implement ProxyHandler in {assembly} from {assembly.Location}.\n" +
                $"Available types: {availableTypes}");
        }

        public static Regex ConvertToRegex(string stringMatcher) =>
            new Regex(Regex.Escape(stringMatcher).Replace("\\*", ".*"), RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private HandlerConfig? _handlerConfig;

        private HandlerConfig HandlerConfig {
            get {
                if (_handlerConfig == null) {
                    _handlerConfig = new HandlerConfig();
                    Configuration.Bind(_handlerConfig);
                }
                if (_handlerConfig == null || !_handlerConfig.Handlers.Any()) {
                    throw new InvalidDataException("The configuraiton must contain at least one handler");
                }
                return _handlerConfig;
            }
        }

        private IConfigurationRoot Configuration { get => ConfigurationFactory.Value; }

        private readonly Lazy<IConfigurationRoot> ConfigurationFactory = new(() =>
            new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .Build()
        );
    }

    internal class HandlerConfig {
        public List<HandlerReference> Handlers { get; set; } = new();
        public List<string> UrlsToWatch { get; set; } = new();
    }

    internal class HandlerReference {
        public string? ConfigSection { get; set; }
        public string HandlerPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string>? UrlsToWatch { get; set; }
    }
}
