// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.CommandLine.Invocation;
using Titanium.Web.Proxy.EventArguments;

namespace Microsoft.Graph.DeveloperProxy.Abstractions;

public interface IProxyContext {
    IProxyConfiguration Configuration { get; }
    ILogger Logger { get; }
}

public class ProxyHttpEventArgsBase {
    internal ProxyHttpEventArgsBase(SessionEventArgs session) =>
        Session = session ?? throw new ArgumentNullException(nameof(session));

    public SessionEventArgs Session { get; }

    public bool HasRequestUrlMatch(ISet<UrlToWatch> watchedUrls) {
        var match = watchedUrls.FirstOrDefault(r => r.Url.IsMatch(Session.HttpClient.Request.RequestUri.AbsoluteUri));
        return match is not null && !match.Exclude;
    }
}

public class ProxyRequestArgs : ProxyHttpEventArgsBase {
    public ProxyRequestArgs(SessionEventArgs session, ResponseState responseState) : base(session) {
        ResponseState = responseState ?? throw new ArgumentNullException(nameof(responseState));
    }
    public ResponseState ResponseState { get; }

    public bool ShouldExecute(ISet<UrlToWatch> watchedUrls) =>
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

public class RequestLog {
    public string[] Message { get; set; }
    public MessageType MessageType { get; set; }
    public LoggingContext? Context { get; set; }

    public RequestLog(string[] message, MessageType messageType, LoggingContext? context)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        MessageType = messageType;
        Context = context;
    }
}

public class RecordingArgs {
    public RecordingArgs(IEnumerable<RequestLog> requestLogs) {
        RequestLogs = requestLogs ?? throw new ArgumentNullException(nameof(requestLogs));
    }
    public IEnumerable<RequestLog> RequestLogs { get; set; }
}

public class RequestLogArgs {
    public RequestLogArgs(RequestLog requestLog) {
        RequestLog = requestLog ?? throw new ArgumentNullException(nameof(requestLog));
    }
    public RequestLog RequestLog { get; set; }
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
    event AsyncEventHandler<ProxyRequestArgs> BeforeRequest;
    /// <summary>
    /// Raised after the response is received from the server.
    /// Is not raised if a response is set during the BeforeRequest event.
    /// Allows plugins to modify a response received from the server.
    /// </summary>
    event AsyncEventHandler<ProxyResponseArgs> BeforeResponse;
    /// <summary>
    /// Raised after a response is sent to the client.
    /// Raised for all responses
    /// </summary>
    event AsyncEventHandler<ProxyResponseArgs>? AfterResponse;
    /// <summary>
    /// Raised after request message has been logged.
    /// </summary>
    event EventHandler<RequestLogArgs>? AfterRequestLog;
    /// <summary>
    /// Raised after recording request logs has stopped.
    /// </summary>
    event EventHandler<RecordingArgs>? AfterRecordingStop;
}

public class PluginEvents : IPluginEvents {
    /// <inheritdoc />
    public event EventHandler<InitArgs>? Init;
    /// <inheritdoc />
    public event EventHandler<OptionsLoadedArgs>? OptionsLoaded;
    /// <inheritdoc />
    public event AsyncEventHandler<ProxyRequestArgs>? BeforeRequest;
    /// <inheritdoc />
    public event AsyncEventHandler<ProxyResponseArgs>? BeforeResponse;
    /// <inheritdoc />
    public event AsyncEventHandler<ProxyResponseArgs>? AfterResponse;
    /// <inheritdoc />
    public event EventHandler<RequestLogArgs>? AfterRequestLog;
    /// <inheritdoc />
    public event EventHandler<RecordingArgs>? AfterRecordingStop;

    public void RaiseInit(InitArgs args) {
        Init?.Invoke(this, args);
    }

    public void RaiseOptionsLoaded(OptionsLoadedArgs args) {
        OptionsLoaded?.Invoke(this, args);
    }

    public void RaiseProxyBeforeRequest(ProxyRequestArgs args) {
        BeforeRequest?.Invoke(this, args);
    }

    public async Task RaiseProxyBeforeResponse(ProxyResponseArgs args) {
        await BeforeResponse?.InvokeAsync(this, args, null);
    }

    public void RaiseProxyAfterResponse(ProxyResponseArgs args) {
        AfterResponse?.Invoke(this, args);
    }

    public void RaiseRequestLogged(RequestLogArgs args) {
        AfterRequestLog?.Invoke(this, args);
    }

    public void RaiseRecordingStopped(RecordingArgs args) {
        AfterRecordingStop?.Invoke(this, args);
    }
}
