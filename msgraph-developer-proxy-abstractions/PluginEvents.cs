// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;

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
    public ProxyResponseArgs(SessionEventArgs session, Request request, ResponseState responseState) : base(session) {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }
    public Request Request { get; }
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
    event EventHandler<InitArgs> Init;
    event EventHandler<OptionsLoadedArgs> OptionsLoaded;
    event EventHandler<ProxyRequestArgs> Request;
    event EventHandler<ProxyResponseArgs> Response;
}

public class PluginEvents : IPluginEvents {
    public event EventHandler<InitArgs>? Init;
    public event EventHandler<OptionsLoadedArgs>? OptionsLoaded;
    public event EventHandler<ProxyRequestArgs>? Request;
    public event EventHandler<ProxyResponseArgs>? Response;

    public void RaiseInit(InitArgs args) {
        Init?.Invoke(this, args);
    }

    public void RaiseOptionsLoaded(OptionsLoadedArgs args) {
        OptionsLoaded?.Invoke(this, args);
    }

    public void RaiseProxyRequest(ProxyRequestArgs args) {
        Request?.Invoke(this, args);
    }

    public void RaiseProxyResponse(ProxyResponseArgs args) {
        Response?.Invoke(this, args);
    }
}
