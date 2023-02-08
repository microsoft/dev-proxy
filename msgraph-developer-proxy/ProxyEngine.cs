// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Helpers;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.Graph.DeveloperProxy;

public class ProxyEngine {
    private readonly PluginEvents _pluginEvents;
    private readonly ILogger _logger;
    private readonly ProxyConfiguration _config;
    private ProxyServer? _proxyServer;
    private ExplicitProxyEndPoint? _explicitEndPoint;
    // lists of URLs to watch, used for intercepting requests
    private ISet<Regex> _urlsToWatch = new HashSet<Regex>();
    // lists of hosts to watch extracted from urlsToWatch,
    // used for deciding which URLs to decrypt for further inspection
    private ISet<Regex> _hostsToWatch = new HashSet<Regex>();

    private static string _productVersion = string.Empty;
    public static string ProductVersion {
        get {
            if (_productVersion == string.Empty) {
                var assemblyPath = Process.GetCurrentProcess()?.MainModule?.FileName ?? typeof(ProxyEngine).Assembly.Location;
                var fileVersionInfo = FileVersionInfo.GetVersionInfo(assemblyPath);
                _productVersion = fileVersionInfo?.ProductVersion!;
            }

            return _productVersion;
        }
    }

    public ProxyEngine(ProxyConfiguration config, ISet<Regex> urlsToWatch, PluginEvents pluginEvents, ILogger logger) {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _urlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
        _pluginEvents = pluginEvents ?? throw new ArgumentNullException(nameof(pluginEvents));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Run(CancellationToken? cancellationToken) {
        if (!_urlsToWatch.Any()) {
            _logger.LogInfo("No URLs to watch configured. Please add URLs to watch in the appsettings.json config file.");
            return;
        }

        LoadHostNamesFromUrls();

        _proxyServer = new ProxyServer();

        _proxyServer.CertificateManager.CertificateStorage = new CertificateDiskCache();
        _proxyServer.BeforeRequest += OnRequest;
        _proxyServer.BeforeResponse += OnBeforeResponse;
        _proxyServer.AfterResponse += OnAfterResponse;
        _proxyServer.ServerCertificateValidationCallback += OnCertificateValidation;
        _proxyServer.ClientCertificateSelectionCallback += OnCertificateSelection;
        cancellationToken?.Register(OnCancellation);

        _explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Any, _config.Port, true);
        if (!RunTime.IsWindows) {
            // we need to change this to a value lower than 397
            // to avoid the ERR_CERT_VALIDITY_TOO_LONG error in Edge
            _proxyServer.CertificateManager.CertificateValidDays = 365;
            // we need to call it explicitly for non-Windows OSes because it's
            // a part of the SetAsSystemHttpProxy that works only on Windows
            _proxyServer.CertificateManager.EnsureRootCertificate();
        }

        // Fired when a CONNECT request is received
        _explicitEndPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequest;

        _proxyServer.AddEndPoint(_explicitEndPoint);
        _proxyServer.Start();

        foreach (var endPoint in _proxyServer.ProxyEndPoints) {
            _logger.LogInfo($"Listening on {endPoint.IpAddress}:{endPoint.Port}...");
        }

        if (RunTime.IsWindows) {
            // Only explicit proxies can be set as system proxy!
            _proxyServer.SetAsSystemHttpProxy(_explicitEndPoint);
            _proxyServer.SetAsSystemHttpsProxy(_explicitEndPoint);
        }
        else {
            _logger.LogWarn("Configure your operating system to use this proxy's port and address");
        }

        _logger.LogInfo("Press CTRL+C to stop the Microsoft Graph Developer Proxy");
        _logger.LogInfo("");
        Console.CancelKeyPress += Console_CancelKeyPress;
        // wait for the proxy to stop
        Console.ReadLine();
        while (_proxyServer.ProxyRunning) { Thread.Sleep(10); }
    }

    // Convert strings from config to regexes.
    // From the list of URLs, extract host names and convert them to regexes.
    // We need this because before we decrypt a request, we only have access
    // to the host name, not the full URL.
    private void LoadHostNamesFromUrls() {
        foreach (var url in _urlsToWatch) {
            // extract host from the URL
            string urlToWatch = Regex.Unescape(url.ToString());
            string hostToWatch;
            if (urlToWatch.ToString().Contains("://")) {
                // if the URL contains a protocol, extract the host from the URL
                hostToWatch = urlToWatch.Split("://")[1].Substring(0, urlToWatch.Split("://")[1].IndexOf("/"));
            }
            else {
                // if the URL doesn't contain a protocol,
                // we assume the whole URL is a host name
                hostToWatch = urlToWatch;
            }

            var hostToWatchRegexString = Regex.Escape(hostToWatch).Replace("\\*", ".*");
            Regex hostRegex = new Regex(hostToWatchRegexString, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // don't add the same host twice
            if (!_hostsToWatch.Contains(hostRegex)) {
                _hostsToWatch.Add(hostRegex);
            }
        }
    }

    private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
        StopProxy();
    }

    private void StopProxy() {
        // Unsubscribe & Quit
        try {
            if (_explicitEndPoint != null) {
                _explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
            }

            if (_proxyServer is not null) {
                _proxyServer.BeforeRequest -= OnRequest;
                _proxyServer.BeforeResponse -= OnBeforeResponse;
                _proxyServer.AfterResponse -= OnAfterResponse;
                _proxyServer.ServerCertificateValidationCallback -= OnCertificateValidation;
                _proxyServer.ClientCertificateSelectionCallback -= OnCertificateSelection;

                _proxyServer.Stop();
            }
        }
        catch (Exception ex) {
            _logger.LogError($"Exception: {ex.Message}");
        }
    }

    private void OnCancellation() {
        if (_explicitEndPoint is not null) {
            _explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
        }

        if (_proxyServer is not null) {
            _proxyServer.BeforeRequest -= OnRequest;
            _proxyServer.BeforeResponse -= OnBeforeResponse;
            _proxyServer.AfterResponse -= OnAfterResponse;
            _proxyServer.ServerCertificateValidationCallback -= OnCertificateValidation;
            _proxyServer.ClientCertificateSelectionCallback -= OnCertificateSelection;

            _proxyServer.Stop();
        }
    }

    async Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e) {
        // Ensures that only the targeted Https domains are proxyied
        if (!IsProxiedHost(e.HttpClient.Request.RequestUri.Host)) {
            e.DecryptSsl = false;
        }
        await Task.CompletedTask;
    }

    async Task OnRequest(object sender, SessionEventArgs e) {
        var method = e.HttpClient.Request.Method.ToUpper();
        // The proxy does not intercept or alter OPTIONS requests
        if (method is not "OPTIONS" && IsProxiedHost(e.HttpClient.Request.RequestUri.Host)) {
            e.UserData = e.HttpClient.Request;
            _logger.LogRequest(new[] { $"{e.HttpClient.Request.Method} {e.HttpClient.Request.Url}" }, MessageType.InterceptedRequest);
            HandleRequest(e);
        }
    }

    private void HandleRequest(SessionEventArgs e) {
        ResponseState responseState = new ResponseState();
        _pluginEvents.RaiseProxyBeforeRequest(new ProxyRequestArgs(e, responseState));

        // We only need to set the proxy header if the proxy has not set a response and the request is going to be sent to the target.
        if (!responseState.HasBeenSet) {
            AddProxyHeader(e.HttpClient.Request);
        }
    }

    private static void AddProxyHeader(Request r) => r.Headers?.AddHeader("Via", $"{r.HttpVersion} graph-proxy/{ProductVersion}");

    private bool IsProxiedHost(string hostName) => _hostsToWatch.Any(h => h.IsMatch(hostName));

    // Modify response
    async Task OnBeforeResponse(object sender, SessionEventArgs e) {
        // read response headers
        if (IsProxiedHost(e.HttpClient.Request.RequestUri.Host)) {
            _pluginEvents.RaiseProxyBeforeResponse(new ProxyResponseArgs(e, new ResponseState()));
        }
    }
    async Task OnAfterResponse(object sender, SessionEventArgs e) {
        // read response headers
        if (IsProxiedHost(e.HttpClient.Request.RequestUri.Host)) {
            _pluginEvents.RaiseProxyAfterResponse(new ProxyResponseArgs(e, new ResponseState()));
        }
    }

    // Allows overriding default certificate validation logic
    Task OnCertificateValidation(object sender, CertificateValidationEventArgs e) {
        // set IsValid to true/false based on Certificate Errors
        if (e.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None) {
            e.IsValid = true;
        }

        return Task.CompletedTask;
    }

    // Allows overriding default client certificate selection logic during mutual authentication
    Task OnCertificateSelection(object sender, CertificateSelectionEventArgs e) {
        // set e.clientCertificate to override
        return Task.CompletedTask;
    }
}