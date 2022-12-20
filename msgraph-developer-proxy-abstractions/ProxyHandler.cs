// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Asn1.Cmp;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Drawing;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.EventArguments;

public interface ILogger {
    public void Log(string message);
    public void LogWarn(string message);
    public void LogError(string message);
}

public class ConsoleLogger : ILogger {
    private readonly ConsoleColor _color;

    public ConsoleLogger() {
        _color = Console.ForegroundColor;
    }

    public void Log(string message) {
        Console.WriteLine(message);
    }

    public void LogWarn(string message) {

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"\tWARNING: {message}");
        Console.ForegroundColor = _color;
    }

    public void LogError(string message) {
        Console.Error.WriteLine(message);
    }
}

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

public class ProxyRequestArgs: ProxyHttpEventArgsBase {

    public ProxyRequestArgs(SessionEventArgs session, ResponseState responseState): base(session) {
        ResponseState = responseState ?? throw new ArgumentNullException(nameof(responseState));
    }
    public ResponseState ResponseState { get; }

    public bool ShouldExecute(ISet<Regex> watchedUrls) => 
        !ResponseState.HasBeenSet 
        && HasRequestUrlMatch(watchedUrls);

}

public class ProxyResponseArgs: ProxyHttpEventArgsBase {
    public ProxyResponseArgs(SessionEventArgs session): base(session) {
    }
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

    public void FireInit(InitArgs args) {
        Init?.Invoke(this, args);
    }

    public void FireOptionsLoaded(OptionsLoadedArgs args) {
        OptionsLoaded?.Invoke(this, args);
    }

    public void FireProxyRequest(ProxyRequestArgs args) {
        Request?.Invoke(this, args);
    }

    public void FireProxyResponse(ProxyResponseArgs args) { 
        Response?.Invoke(this, args); 
    }
}

public interface IProxyPlugin {
    string Name { get; }
    void Register(IPluginEvents pluginEvents,
                  IProxyContext context,
                  ISet<Regex> urlsToWatch,
                  IConfigurationSection? configSection = null);
}