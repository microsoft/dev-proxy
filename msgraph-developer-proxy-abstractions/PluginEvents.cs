// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.EventArguments;

namespace Microsoft.Graph.DeveloperProxy.Abstractions;

public interface IProxyContext {
    public ILogger Logger { get; }
}

public class ProxyHttpEventArgsBase {
    internal ProxyHttpEventArgsBase(SessionEventArgs session) =>
        Session = session ?? throw new ArgumentNullException(nameof(session));

    public SessionEventArgs Session { get; }

    public bool HasRequestUrlMatch(ISet<Regex> watchedUrls) =>
        watchedUrls.Any(r => r.IsMatch(Session.HttpClient.Request.RequestUri.AbsoluteUri));
}

public class ProxyRequestArgs : ProxyHttpEventArgsBase {
    public ProxyRequestArgs(SessionEventArgs session, ResponseState responseState) : base(session) {
        ResponseState = responseState ?? throw new ArgumentNullException(nameof(responseState));
    }
    public ResponseState ResponseState { get; }

    public bool ShouldExecute(ISet<Regex> watchedUrls) =>
        !ResponseState.HasBeenSet
        && HasRequestUrlMatch(watchedUrls);
}

public class ProxyResponseArgs : ProxyHttpEventArgsBase {
    public ProxyResponseArgs(SessionEventArgs session, ResponseState responseState) : base(session) {
        ResponseState = responseState ?? throw new ArgumentNullException(nameof(responseState));
    }
    public ResponseState ResponseState { get; }
}

public class InitArgs {
    public InitArgs(RootCommand rootCommand) {
        RootCommand = rootCommand ?? throw new ArgumentNullException(nameof(rootCommand));
    }
    public RootCommand RootCommand { get; set; }

}

public class OptionsLoadedArgs {
    public OptionsLoadedArgs(InvocationContext context) {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }
    public InvocationContext Context { get; set; }
}

public interface IPluginEvents {
    /// <summary>
    /// Raised while starting the proxy, allows plugins to register command line options
    /// </summary>
    event EventHandler<InitArgs> Init;
    /// <summary>
    /// Raised during startup after command line arguments have been parsed,
    /// used to update the internal state of a plugin that registers command line options
    /// </summary>
    event EventHandler<OptionsLoadedArgs> OptionsLoaded;
    /// <summary>
    /// Raised before a request is sent to the server.
    /// Used to intercept requests.
    /// </summary>
    event EventHandler<ProxyRequestArgs> BeforeRequest;
    /// <summary>
    /// Raised after the response is received from the server.
    /// Is not raised if a response is set during the BeforeRequest event.
    /// Allows plugins to modify a response received from the server.
    /// </summary>
    event EventHandler<ProxyResponseArgs> BeforeResponse;
    /// <summary>
    /// Raised after a response is sent to the client.
    /// Raised for all responses
    /// </summary>
    event EventHandler<ProxyResponseArgs>? AfterResponse;
}

public class PluginEvents : IPluginEvents {
    /// <inheritdoc />
    public event EventHandler<InitArgs>? Init;
    /// <inheritdoc />
    public event EventHandler<OptionsLoadedArgs>? OptionsLoaded;
    /// <inheritdoc />
    public event EventHandler<ProxyRequestArgs>? BeforeRequest;
    /// <inheritdoc />
    public event EventHandler<ProxyResponseArgs>? BeforeResponse;
    /// <inheritdoc />
    public event EventHandler<ProxyResponseArgs>? AfterResponse;

    public void RaiseInit(InitArgs args) {
        Init?.Invoke(this, args);
    }

    public void RaiseOptionsLoaded(OptionsLoadedArgs args) {
        OptionsLoaded?.Invoke(this, args);
    }

    public void RaiseProxyBeforeRequest(ProxyRequestArgs args) {
        BeforeRequest?.Invoke(this, args);
    }

    public void RaiseProxyBeforeResponse(ProxyResponseArgs args) {
        BeforeResponse?.Invoke(this, args);
    }

    public void RaiseProxyAfterResponse(ProxyResponseArgs args) {
        AfterResponse?.Invoke(this, args);
    }
}
