# Support a "plug-in" model to provide extensibility

To allow developers to provide unique handling behaviors for different APIs the Microsoft Graph Developer Proxy will have a plug-in model provide an extension point for developer. This will provide separation of concerns between the software components intercepting network request and those providing behaviors.
To enable developers building plugins an abstractions library will be distributed on NuGet.

## History

| Version | Date | Comments | Author |
| ------- | ---- | -------- | ------ |
| 1.0 | 2022-12-19 | Initial specifications | @gavinbarron |

## Plug-in system

The plugin system will leverage reflection to load the custom logic at runtime via the supplied configuration.
The plugin system will leverage `AssemblyLoadContext` from `System.Runtime.Loader` to allow for isolated loading of plugins and their dependencies. The approach will be based upon https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support

Should a the proxy be unable to load a plugin based upon the supplied configuration the proxy will exit with an error informing the user that the plugin was not able to be found.

Plugins will utilize event listeners to provide a decoupled and late bound integration between the core proxy logic and the logic that the plugins provide.

## Configuration file

The existing `appsettings.json` file will be used to provide configuration. Both for determining which plugins are loaded into the proxy and providing configuration for each of the loaded plugins. The set of plugins to be loaded will be specified as an array of objects at the root of the configuration file in the `plugins` property. Each plugin must supply a `pluginPath` and `name` and may optionally provide `configSection` and `urlsToWatch` in the plugins array.
- `pluginPath` - required, a string which is the relative path to the assembly containing the class definition for the plugin which will be loaded using reflection.
- `name` - required, a string identifier for the plugin class, allows developer to ship multiple plugins in a single assembly. This should correspond to the value returned by the `Name` property defined on the plugin class. Plugin names must be unique per-assembly.
- `configSection` - optional, a string for which there must be a matching object property at the root of the configuration file. If a plugin supplies a `configSection` and no corresponding property exists in the configuration file an error will be thrown during start up of the proxy.
- `urlsToWatch` - optional, an array of strings defining the urls for which this plugin will watch instead of the `urlsToWatch` at the root of the config file, this behavior is defined in the [Multi URL support spec](./multi-url-support.md).
- `enabled` - optional boolean when `false` the proxy will not attempt to load this plugin. `true` by default.

```json
  "plugins": [
    {
        "configSection": "randomErrors",
        "pluginPath": "RandomErrorPlugin\\msgraph-proxy-handler.dll",
        "name": "RandomErrorPlugin",
        "urlsToWatch": [
            "https://graph.microsoft.com/v1.0/*",
            "https://graph.microsoft.com/beta/*"
        ],
        "enabled": true
    }
  ],
  "randomErrors": {
    "rate": 50,
    "allowedErrors": [ 429, 500, 502, 503, 504, 507 ]
  }
```

Plugins will be executed in the order that they are listed in the plugins configuration array. At least one plugin must be present in the configuration, otherwise the proxy will exit with an error informing the user that their configuration is invalid and contains no plugins.

## Plugins

Plugin classes must implement the `IProxyPlugin` interface and provide a parameterless constructor.

```cs
public interface IProxyPlugin {
    string Name { get; }
    void Register(IPluginEvents pluginEvents,
                  IProxyContext context,
                  ISet<Regex> urlsToWatch,
                  IConfigurationSection? configSection);
}

public interface IPluginEvents {
    event EventHandler<InitArgs> Init;
    event EventHandler<OptionsLoadedArgs> OptionsLoaded;
    event EventHandler<ProxyRequestArgs> Request;
    event EventHandler<ProxyResponseArgs> Response;
}

public interface IPluginContext {
    ILogger Logger { get; }
}
```

### Registration

The `Register` method of an `IProxyPlugin` will be called after an instance is created and will provide an `ISet<Regex>` to define the urls the plugin wll watch, either using the default or a plugin specific set based on the supplied configuration, an `IProxyContext` to supply utility objects from the proxy, and an `IConfigurationSection` if one was loaded.

> It is strongly recommended that plugin implementers provide a default configuration in their code.

Plugin classes should store the supplied `IProxyContext` for use when events are fired. At a minimum this context will supply the `ILogger` which plugins will use to provide output messages. The contents of the context are likely to evolve as new requirements for plugin emerge.

The details of the ILogger interface and implementation are out of the scope of this spec, except to say that they will provide plugins with a standardized mechanism for logging messages using preset formatting and categorization.

Plugin classes may register a handler for any events provided in the `IPluginEvents` interface. Plugins registering a handler for the `Init` event should also register a handler for the `OptionsLoaded` event to consume the supplied options.

A set of static utility methods will be provided via the `ProxyUtils` class. A proposed initial implementation of the `ProxyUtils` class is provided here:

```cs
public static class ProxyUtils {

    public static bool IsSdkRequest(Request request) {
        return request.Headers.HeaderExists("SdkVersion");
    }

    public static bool IsGraphRequest(Request request) {
        return request.RequestUri.Host.Contains("graph", StringComparison.OrdinalIgnoreCase);
    }
}
```

It is anticipated that the set of helper methods offered in the `ProxyUtils` class will grow and change over time.

### Init

This event provides the plugin with a `RootCommand` from `System.CommandLine`. This enables plugins to register options to read parameters from the supplied command line arguments using `Option<T>`.

### OptionsLoaded

This event provides the plugin with any values supplied for options registered on the `RootCommand` during `Init`. The internal configuration state should be updated with these options by implementers.

### Request

The event is fired when the proxy intercepts an HTTP request.Implementers should check the ResponseState on the suppied event args and the url the request is targeting against the set of urlsToWatch supplied during registration to determine if their plugin logic should execute.

An helper method is provided on the event args object can be used like so: `e.ShouldExecute(this.urlsToWatch))`

### Response

This event is fired before a response is sent from the proxy to the request originator.

This allows plugin implementers to modify the response being sent to the caller of the HTTP API. For example this could be used to modify response headers to simulate rate limit resource consumption.

Again, implementers should check that the response against the urlsToWatch supplied during registration to determine if the plugin should execute.

In this instance, the event args object supplies a `e.HasRequestUrlMatch(this.urlsToWatch))` helper method.

## Sample plugin implementation

```cs
public class SelectGuidancePlugin : IProxyPlugin {
    private ISet<Regex>? _urlsToWatch;
    private ILogger? _logger;
    public string Name => nameof(SelectGuidancePlugin);

    public void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<Regex> urlsToWatch,
                            IConfigurationSection? configSection = null) {
        if (pluginEvents is null) {
            throw new ArgumentNullException(nameof(pluginEvents));
        }

        if (context is null || context.Logger is null) {
            throw new ArgumentException($"{nameof(context)} must not be null and must supply a non-null Logger", nameof(context));
        }

        if (urlsToWatch is null || urlsToWatch.Count == 0) {
            throw new ArgumentException($"{nameof(urlsToWatch)} cannot be null or empty", nameof(urlsToWatch));
        }

        _urlsToWatch = urlsToWatch;
        _logger = context.Logger;

        pluginEvents.Request += OnRequest;
    }

    private void OnRequest(object? sender, ProxyRequestArgs e) {
        Request request = e.Session.HttpClient.Request;
        if (_urlsToWatch is not null && e.ShouldExecute(_urlsToWatch) && WarnNoSelect(request))
            _logger?.LogWarn(BuildUseSelectMessage(request));
    }

    private static bool WarnNoSelect(Request request) =>
        ProxyUtils.IsGraphRequest(request) &&
            request.Method == "GET" &&
            !request.Url.Contains("$select", StringComparison.OrdinalIgnoreCase);

    private static string GetSelectParameterGuidanceUrl() => "https://learn.microsoft.com/graph/query-parameters#select-parameter";
    private static string BuildUseSelectMessage(Request r) => $"To improve performance of your application, use the $select parameter when calling {r.RequestUriString}. More info at {GetSelectParameterGuidanceUrl()}";
}
```
