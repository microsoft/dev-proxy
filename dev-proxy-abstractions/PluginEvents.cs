// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Security.Cryptography.X509Certificates;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;

namespace Microsoft.DevProxy.Abstractions;

public interface IProxyContext
{
    IProxyConfiguration Configuration { get; }
    IProxyLogger Logger { get; }
    X509Certificate2? Certificate { get; }
}

public class ThrottlerInfo
{
    /// <summary>
    /// Throttling key used to identify which requests should be throttled.
    /// Can be set to a hostname, full URL or a custom string value, that
    /// represents for example a portion of the API
    /// </summary>
    public string ThrottlingKey { get; private set; }
    /// <summary>
    /// Function responsible for matching the request to the throttling key.
    /// Takes as arguments:
    /// - intercepted request
    /// - the throttling key
    /// Returns an instance of ThrottlingInfo that contains information
    /// whether the request should be throttled or not.
    /// </summary>
    public Func<Request, string, ThrottlingInfo> ShouldThrottle { get; private set; }
    /// <summary>
    /// Time when the throttling window will be reset
    /// </summary>
    public DateTime ResetTime { get; set; }

    public ThrottlerInfo(string throttlingKey, Func<Request, string, ThrottlingInfo> shouldThrottle, DateTime resetTime)
    {
        ThrottlingKey = throttlingKey ?? throw new ArgumentNullException(nameof(throttlingKey));
        ShouldThrottle = shouldThrottle ?? throw new ArgumentNullException(nameof(shouldThrottle));
        ResetTime = resetTime;
    }
}

public class ThrottlingInfo
{
    public int ThrottleForSeconds { get; set; }
    public string RetryAfterHeaderName { get; set; }

    public ThrottlingInfo(int throttleForSeconds, string retryAfterHeaderName)
    {
        ThrottleForSeconds = throttleForSeconds;
        RetryAfterHeaderName = retryAfterHeaderName ?? throw new ArgumentNullException(nameof(retryAfterHeaderName));
    }
}

public class ProxyHttpEventArgsBase
{
    internal ProxyHttpEventArgsBase(SessionEventArgs session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public SessionEventArgs Session { get; }
    public Dictionary<string, object> SessionData { get; set; } = new Dictionary<string, object>();
    public Dictionary<string, object> GlobalData { get; set; } = new Dictionary<string, object>();

    public bool HasRequestUrlMatch(ISet<UrlToWatch> watchedUrls)
    {
        var match = watchedUrls.FirstOrDefault(r => r.Url.IsMatch(Session.HttpClient.Request.RequestUri.AbsoluteUri));
        return match is not null && !match.Exclude;
    }
}

public class ProxyRequestArgs : ProxyHttpEventArgsBase
{
    public ProxyRequestArgs(SessionEventArgs session, ResponseState responseState) : base(session)
    {
        ResponseState = responseState ?? throw new ArgumentNullException(nameof(responseState));
    }
    public ResponseState ResponseState { get; }

    public bool ShouldExecute(ISet<UrlToWatch> watchedUrls) =>
        !ResponseState.HasBeenSet
        && HasRequestUrlMatch(watchedUrls);
}

public class ProxyResponseArgs : ProxyHttpEventArgsBase
{
    public ProxyResponseArgs(SessionEventArgs session, ResponseState responseState) : base(session)
    {
        ResponseState = responseState ?? throw new ArgumentNullException(nameof(responseState));
    }
    public ResponseState ResponseState { get; }
}

public class InitArgs
{
    public InitArgs()
    {
    }
}

public class OptionsLoadedArgs
{
    public InvocationContext Context { get; set; }
    public Option[] Options { get; set; }

    public OptionsLoadedArgs(InvocationContext context, Option[] options)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }
}

public class RequestLog
{
    public string[] MessageLines { get; set; }
    public MessageType MessageType { get; set; }
    public LoggingContext? Context { get; set; }

    public RequestLog(string[] messageLines, MessageType messageType, LoggingContext? context)
    {
        MessageLines = messageLines ?? throw new ArgumentNullException(nameof(messageLines));
        MessageType = messageType;
        Context = context;
    }
}

public class RecordingArgs
{
    public RecordingArgs(IEnumerable<RequestLog> requestLogs)
    {
        RequestLogs = requestLogs ?? throw new ArgumentNullException(nameof(requestLogs));
    }
    public IEnumerable<RequestLog> RequestLogs { get; set; }
}

public class RequestLogArgs
{
    public RequestLogArgs(RequestLog requestLog)
    {
        RequestLog = requestLog ?? throw new ArgumentNullException(nameof(requestLog));
    }
    public RequestLog RequestLog { get; set; }
}

public interface IPluginEvents
{
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
    event AsyncEventHandler<RecordingArgs>? AfterRecordingStop;
    /// <summary>
    /// Raised when user requested issuing mock requests.
    /// </summary>
    event AsyncEventHandler<EventArgs>? MockRequest;
}

public class PluginEvents : IPluginEvents
{
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
    public event AsyncEventHandler<RecordingArgs>? AfterRecordingStop;
    public event AsyncEventHandler<EventArgs>? MockRequest;

    public void RaiseInit(InitArgs args)
    {
        Init?.Invoke(this, args);
    }

    public void RaiseOptionsLoaded(OptionsLoadedArgs args)
    {
        OptionsLoaded?.Invoke(this, args);
    }

    public async Task RaiseProxyBeforeRequest(ProxyRequestArgs args)
    {
        if (BeforeRequest is not null)
        {
            await BeforeRequest.InvokeAsync(this, args, null);
        }
    }

    public async Task RaiseProxyBeforeResponse(ProxyResponseArgs args)
    {
        if (BeforeResponse is not null)
        {
            await BeforeResponse.InvokeAsync(this, args, null);
        }
    }

    public async Task RaiseProxyAfterResponse(ProxyResponseArgs args)
    {
        if (AfterResponse is not null)
        {
            await AfterResponse.InvokeAsync(this, args, null);
        }
    }

    public void RaiseRequestLogged(RequestLogArgs args)
    {
        AfterRequestLog?.Invoke(this, args);
    }

    public async Task RaiseRecordingStopped(RecordingArgs args)
    {
        if (AfterRecordingStop is not null)
        {
            await AfterRecordingStop.InvokeAsync(this, args, null);
        }
    }

    public async Task RaiseMockRequest(EventArgs args)
    {
        if (MockRequest is not null)
        {
            await MockRequest.InvokeAsync(this, args, null);
        }
    }
}
