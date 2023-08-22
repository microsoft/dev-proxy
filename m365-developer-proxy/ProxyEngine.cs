// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft365.DeveloperProxy.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Helpers;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft365.DeveloperProxy;

public class ProxyEngine {
    private readonly PluginEvents _pluginEvents;
    private readonly ILogger _logger;
    private readonly ProxyConfiguration _config;
    private ProxyServer? _proxyServer;
    private ExplicitProxyEndPoint? _explicitEndPoint;
    // lists of URLs to watch, used for intercepting requests
    private ISet<UrlToWatch> _urlsToWatch = new HashSet<UrlToWatch>();
    // lists of hosts to watch extracted from urlsToWatch,
    // used for deciding which URLs to decrypt for further inspection
    private ISet<UrlToWatch> _hostsToWatch = new HashSet<UrlToWatch>();
    private static Assembly? _assembly;
    private IList<ThrottlerInfo> _throttledRequests = new List<ThrottlerInfo>();

    internal static Assembly GetAssembly()
            => _assembly ??= (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

    private static string _productVersion = string.Empty;
    public static string ProductVersion {
        get {
            if (_productVersion == string.Empty) {
                var assembly = GetAssembly();
                var assemblyVersionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

                if (assemblyVersionAttribute is null) {
                    _productVersion = assembly.GetName().Version?.ToString() ?? "";
                }
                else {
                    _productVersion = assemblyVersionAttribute.InformationalVersion;
                }
            }

            return _productVersion;
        }
    }

    private bool _isRecording = false;
    private List<RequestLog> _requestLogs = new List<RequestLog>();

    public ProxyEngine(ProxyConfiguration config, ISet<UrlToWatch> urlsToWatch, PluginEvents pluginEvents, ILogger logger) {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _urlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
        _pluginEvents = pluginEvents ?? throw new ArgumentNullException(nameof(pluginEvents));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Run(CancellationToken? cancellationToken) {
        if (!_urlsToWatch.Any()) {
            _logger.LogInfo("No URLs to watch configured. Please add URLs to watch in the m365proxyrc.json config file.");
            return;
        }

        LoadHostNamesFromUrls();

        // for background db refresh, let's use a separate logger
        // that only logs warnings and errors
        var _logger2 = (ILogger)_logger.Clone();
        _logger2.LogLevel = LogLevel.Warn;
        // let's not await so that it doesn't block the proxy startup
        MSGraphDbCommandHandler.GenerateMsGraphDb(_logger2, true);

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

        _logger.LogInfo("Press CTRL+C to stop the Microsoft 365 Developer Proxy");
        _logger.LogInfo("");
        Console.CancelKeyPress += Console_CancelKeyPress;

        if (_config.Record) {
            StartRecording();
        }
        _pluginEvents.AfterRequestLog += AfterRequestLog;

        // we need this check or proxy will fail with an exception
        // when run for example in VSCode's integrated terminal
        if (!Console.IsInputRedirected) {
            ReadKeys();
        }
        while (_proxyServer.ProxyRunning) { Thread.Sleep(10); }
    }

    private void AfterRequestLog(object? sender, RequestLogArgs e) {
        if (!_isRecording)
        {
            return;
        }

        _requestLogs.Add(e.RequestLog);
    }

    private void ReadKeys() {
        ConsoleKey key;
        do {
            key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.R) {
                StartRecording();
            }
            if (key == ConsoleKey.S) {
                StopRecording();
            }
            if (key == ConsoleKey.C) {
                Console.Clear();
                Console.WriteLine("Press CTRL+C to stop the Microsoft 365 Developer Proxy");
                Console.WriteLine("");
            }
        } while (key != ConsoleKey.Escape);
    }

    private void StartRecording() {
        if (_isRecording) {
            return;
        }

        _isRecording = true;
        PrintRecordingIndicator();
    }

    private void StopRecording() {
        if (!_isRecording) {
            return;
        }

        _isRecording = false;
        PrintRecordingIndicator();
        // clone the list so that we can clear the original
        // list in case a new recording is started, and
        // we let plugins handle previously recorded requests
        var clonedLogs = _requestLogs.ToArray();
        _requestLogs.Clear();
        _pluginEvents.RaiseRecordingStopped(new RecordingArgs(clonedLogs));
    }

    private void PrintRecordingIndicator() {
        lock (ConsoleLogger.ConsoleLock) {
            if (_isRecording) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.Write("◉");
                Console.ResetColor();
                Console.Error.WriteLine(" Recording... ");
            }
            else {
                Console.Error.WriteLine("○ Stopped recording");
            }
        }
    }

    // Convert strings from config to regexes.
    // From the list of URLs, extract host names and convert them to regexes.
    // We need this because before we decrypt a request, we only have access
    // to the host name, not the full URL.
    private void LoadHostNamesFromUrls() {
        foreach (var urlToWatch in _urlsToWatch) {
            // extract host from the URL
            string urlToWatchPatter = Regex.Unescape(urlToWatch.Url.ToString()).Replace(".*", "*");
            string hostToWatch;
            if (urlToWatchPatter.ToString().Contains("://")) {
                // if the URL contains a protocol, extract the host from the URL
                hostToWatch = urlToWatchPatter.Split("://")[1].Substring(0, urlToWatchPatter.Split("://")[1].IndexOf("/"));
            }
            else {
                // if the URL doesn't contain a protocol,
                // we assume the whole URL is a host name
                hostToWatch = urlToWatchPatter;
            }

            var hostToWatchRegexString = Regex.Escape(hostToWatch).Replace("\\*", ".*");
            Regex hostRegex = new Regex($"^{hostToWatchRegexString}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // don't add the same host twice
            if (!_hostsToWatch.Any(h => h.Url.ToString() == hostRegex.ToString())) {
                _hostsToWatch.Add(new UrlToWatch(hostRegex));
            }
        }
    }

    private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
        StopRecording();
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
        if (!IsProxiedHost(e.HttpClient.Request.RequestUri.Host) ||
            !IsProxiedProcess(e)) {
            e.DecryptSsl = false;
        }
        await Task.CompletedTask;
    }

  private int GetProcessId(TunnelConnectSessionEventArgs e)
  {
    if (RunTime.IsWindows) {
        return e.HttpClient.ProcessId.Value;
    }

    var psi = new ProcessStartInfo {
        FileName = "lsof",
        Arguments = $"-i :{e.ClientRemoteEndPoint.Port}",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    };
    var proc = new Process {
        StartInfo = psi
    };
    proc.Start();
    var output = proc.StandardOutput.ReadToEnd();
    proc.WaitForExit();

    var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
    var matchingLine = lines.FirstOrDefault(l => l.Contains($"{e.ClientRemoteEndPoint.Port}->"));
    if (matchingLine is null) {
        return -1;
    }
    var pidString = Regex.Matches(matchingLine, @"^.*?\s+(\d+)")?.FirstOrDefault()?.Groups[1]?.Value;
    if (pidString is null) {
        return -1;
    }

    var pid = -1;
    if (int.TryParse(pidString, out pid))
    {
        return pid;
    }
    else {
        return -1;
    }
  }

  private bool IsProxiedProcess(TunnelConnectSessionEventArgs e) {
    // If no process names or IDs are specified, we proxy all processes
    if (!_config.WatchPids.Any() &&
        !_config.WatchProcessNames.Any()) {
      return true;
    }

    var processId = GetProcessId(e);
    if (processId == -1) {
      return false;
    }

    if (_config.WatchPids.Any() &&
        _config.WatchPids.Contains(processId)) {
      return true;
    }

    if (_config.WatchProcessNames.Any()) {
      var processName = Process.GetProcessById(processId).ProcessName;
      if (_config.WatchProcessNames .Contains(processName)) {
        return true;
      }
    }
    
    return false;
  }

  async Task OnRequest(object sender, SessionEventArgs e) {
        var method = e.HttpClient.Request.Method.ToUpper();
        // The proxy does not intercept or alter OPTIONS requests
        if (method is not "OPTIONS" && IsProxiedHost(e.HttpClient.Request.RequestUri.Host)) {
            // we need to keep the request body for further processing
            // by plugins
            e.HttpClient.Request.KeepBody = true;
            if (e.HttpClient.Request.HasBody) {
                await e.GetRequestBodyAsString();
            }
            
            e.UserData = e.HttpClient.Request;
            _logger.LogRequest(new[] { $"{e.HttpClient.Request.Method} {e.HttpClient.Request.Url}" }, MessageType.InterceptedRequest, new LoggingContext(e));
            HandleRequest(e);
        }
    }

    private void HandleRequest(SessionEventArgs e) {
        ResponseState responseState = new ResponseState();
        _pluginEvents.RaiseProxyBeforeRequest(new ProxyRequestArgs(e, _throttledRequests, responseState));

        // We only need to set the proxy header if the proxy has not set a response and the request is going to be sent to the target.
        if (!responseState.HasBeenSet) {
            _logger?.LogRequest(new[] { "Passed through" }, MessageType.PassedThrough, new LoggingContext(e));
            AddProxyHeader(e.HttpClient.Request);
        }
    }

    private static void AddProxyHeader(Request r) => r.Headers?.AddHeader("Via", $"{r.HttpVersion} graph-proxy/{ProductVersion}");

    private bool IsProxiedHost(string hostName) => _hostsToWatch.Any(h => h.Url.IsMatch(hostName));


    // Modify response
    async Task OnBeforeResponse(object sender, SessionEventArgs e) {
        // read response headers
        if (IsProxiedHost(e.HttpClient.Request.RequestUri.Host)) {
            await _pluginEvents.RaiseProxyBeforeResponse(new ProxyResponseArgs(e, _throttledRequests, new ResponseState()));
        }
    }
    async Task OnAfterResponse(object sender, SessionEventArgs e) {
        // read response headers
        if (IsProxiedHost(e.HttpClient.Request.RequestUri.Host)) {
            _pluginEvents.RaiseProxyAfterResponse(new ProxyResponseArgs(e, _throttledRequests, new ResponseState()));
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