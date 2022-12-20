# Support a "plug-in" model for request handlers

To allow developers to provide unique handling behaviors for different APIs the Microsoft Graph will have a plug-in model to dynamically load request handlers. This will provide separation of concerns between the software components intercepting network request and those providing responses.
To enable developers building handlers an abstractions library will be distributed on NuGet.

## History

| Version | Date | Comments | Author |
| ------- | -------- | ----- | --- |
| 1.0 | 2022-12-19 | Initial specifications | @gavinbarron |

## Configuration file

The existing `appsettings.json` file will be used to provide configuration. Both for determining which request handlers are loaded into the proxy and providing configuration for each of the loaded request handlers. The set of request handlers to be loaded will be specified as an array of objects at the root of the configuration file in the `handlers` property. Each handler must supply a `configSection`, `handerPath`, `name`, and `urlsToWatch` in the handlers array.
- `configSection` is a string for which there must be a matching object property at the root of the configuration file. If a handler does not provide any configuration options then an empty object must be supplied.
- `handlerPath` a string which is the relative path to the assembly containing the class definition for the handler which will be loaded using reflection.
- `name` a string identifier for the handler class, allows developer to ship multiple handlers in a single assembly.
- `urlsToWatch` is an array of strings defining the urls for which this hander will watch, this behavior is defined in the [Multi URL support spec](./multi-url-support.md).

```json
  "handlers": [
    {
        "configSection": "msGraphProxy",
        "handlerPath": "GraphHandler\\msgraph-proxy-handler.dll",
        "name": "GraphHandler",
        "urlsToWatch": [
            "https://graph.microsoft.com/v1.0/*",
            "https://graph.microsoft.com/beta/*"
        ],
    }
  ],
  "msGraphProxy": {
    // config options specific to the msGraphProxy
  }
```

Handlers will executed in the order that they are listed in the handlers configuration array. At least one handlers must be present in the configuration, otherwise the proxy will exit with an error informing the user that their configuration is invalid and contains no handlers.

## Handlers

Handler classes must inherit from the provider abstract base class `ProxyHandler`, proposed implementation listed below for reference, and must provide an implementation for the `ExecuteInternal` method.
Handlers can set the response which will be provided by the proxy using the `GenericResponse` method of the provided `SessionEventArgs` object, when a handler sets the response it should also set the `IsComplete` property of the provided `ResponseState` to prevent subsequently executed handler from modifying the response.

```cs
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Titanium.Web.Proxy.EventArguments;

public abstract class ProxyHandler
{
    private readonly IEnumerable<Regex> _urlsToWatch;
    private readonly IConfigurationSection _config;

    public ProxyHandler(IEnumerable<Regex> urlsToWatch, IConfigurationSection config)
    {
        this._urlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
        if (!urlsToWatch.Any())
            throw new ArgumentException($"{nameof(urlsToWatch)} cannot be empty", nameof(urlsToWatch));
        this._config = config;
    }

    /**
     * Use this method to add any command line options to the proxy
     */
    public abstract void AddCommandLineOptions(RootCommand command);

    /**
     * Read options from command line args and update internal configuration
     */
    public abstract int UpdateConfiguration(InvocationContext context);

    /**
     * Use this method to provide a concrete implmentation for a request handler.
     * Set ResponseState.HasBeenSet to prevent additional handlers from modifying the response
     */
    protected abstract void ExecuteInternal(SessionEventArgs e, ResponseState state);

    /**
     * Default implementation to determine if the handler should be triggered.
     * Override if you need to ignore the ResponseState.HasBeenSet, typically this would be done
     * when your handler does not mutate the response and must be executed, ususally this can be
     * better handled by configuring the handler to run earlier in the set of configured handlers
     */
    protected virtual bool ShouldTrigger(SessionEventArgs e, ResponseState state) =>
        !state.HasBeenSet &&
        this._urlsToWatch.Any(u => u.IsMatch(e.HttpClient.Request.RequestUriString));

    /**
     * Public entrypoint called by the Proxy for each request.
     */
    public void Excecute(SessionEventArgs e, ResponseState state)
    {
        if (ShouldTrigger(e, state))
            ExecuteInternal(e, state);
    }

}
```

### Required implementations

| Member | kind | purpose | Notes |
| -------| ---- | ------- | ------|
| Name | Read only property | Provides a name to allow developers to ship multiple handlers in a single assembly | Should match the class name and be unique per assembly |
| AddCommandLineOptions | method | Provides typed commandline inputs and validation | Use `Option<T>` from `System.CommandLine` to define command line options |
| UpdateConfiguration | method | Overwrite configuration defaults with supplied command line options | Read values for supplied options from InvocationContext |
| ExecuteInternal | method | Handler specific logic to be applied to matching requests | `ResponseState.HasBeenSet` should be set to true if the response has been set to prevent unwanted changes to the response |

### Plug-in system

The Hander plug-in system will leverage reflection to load the custom logic at runtime via the supplied configuration.
The plug-in system will leverage `AssemblyLoadContext` from `System.Runtime.Loader` to allow for isolated loading of plug-ins and their dependencies. The approach will be based upon https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support

Should a the proxy be unable to load a handler based upon the supplied configuration the proxy will exit with an error informing the user that the handler plug-in was not able to be found.
