﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Helpers;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.DevProxy;

enum ToggleSystemProxyAction
{
    On,
    Off
}

public class ProxyEngine : BackgroundService
{
    private readonly IPluginEvents _pluginEvents;
    private readonly ILogger _logger;
    private readonly IProxyConfiguration _config;
    private static ProxyServer? _proxyServer;
    private ExplicitProxyEndPoint? _explicitEndPoint;
    // lists of URLs to watch, used for intercepting requests
    private ISet<UrlToWatch> _urlsToWatch = new HashSet<UrlToWatch>();
    // lists of hosts to watch extracted from urlsToWatch,
    // used for deciding which URLs to decrypt for further inspection
    private ISet<UrlToWatch> _hostsToWatch = new HashSet<UrlToWatch>();
    private IProxyState _proxyState;
    // Dictionary for plugins to store data between requests
    // the key is HashObject of the SessionEventArgs object
    private Dictionary<int, Dictionary<string, object>> _pluginData = new();

    public static X509Certificate2? Certificate => _proxyServer?.CertificateManager.RootCertificate;

    private ExceptionHandler _exceptionHandler => ex => _logger.LogError(ex, "An error occurred in a plugin");

    static ProxyEngine()
    {
        _proxyServer = new ProxyServer();
        _proxyServer.CertificateManager.RootCertificateName = "Dev Proxy CA";
        _proxyServer.CertificateManager.CertificateStorage = new CertificateDiskCache();
        // we need to change this to a value lower than 397
        // to avoid the ERR_CERT_VALIDITY_TOO_LONG error in Edge
        _proxyServer.CertificateManager.CertificateValidDays = 365;
        _ = _proxyServer.CertificateManager.LoadOrCreateRootCertificateAsync().Result;
    }

    public ProxyEngine(IProxyConfiguration config, ISet<UrlToWatch> urlsToWatch, IPluginEvents pluginEvents, IProxyState proxyState, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _urlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
        _pluginEvents = pluginEvents ?? throw new ArgumentNullException(nameof(pluginEvents));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _proxyState = proxyState ?? throw new ArgumentNullException(nameof(proxyState));
    }

    private void ToggleSystemProxy(ToggleSystemProxyAction toggle, string? ipAddress = null, int? port = null)
    {
        var bashScriptPath = Path.Join(ProxyUtils.AppFolder, "toggle-proxy.sh");
        var args = toggle switch
        {
            ToggleSystemProxyAction.On => $"on {ipAddress} {port}",
            ToggleSystemProxyAction.Off => "off",
            _ => throw new NotImplementedException()
        };

        ProcessStartInfo startInfo = new ProcessStartInfo()
        {
            FileName = "/bin/bash",
            Arguments = $"{bashScriptPath} {args}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process() { StartInfo = startInfo };
        process.Start();
        process.WaitForExit();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Debug.Assert(_proxyServer is not null, "Proxy server is not initialized");

        if (!_urlsToWatch.Any())
        {
            _logger.LogInformation("No URLs to watch configured. Please add URLs to watch in the devproxyrc.json config file.");
            return;
        }

        LoadHostNamesFromUrls();

        _proxyServer.BeforeRequest += OnRequest;
        _proxyServer.BeforeResponse += OnBeforeResponse;
        _proxyServer.AfterResponse += OnAfterResponse;
        _proxyServer.ServerCertificateValidationCallback += OnCertificateValidation;
        _proxyServer.ClientCertificateSelectionCallback += OnCertificateSelection;

        var ipAddress = string.IsNullOrEmpty(_config.IPAddress) ? IPAddress.Any : IPAddress.Parse(_config.IPAddress);
        _explicitEndPoint = new ExplicitProxyEndPoint(ipAddress, _config.Port, true);
        // Fired when a CONNECT request is received
        _explicitEndPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequest;
        if (_config.InstallCert)
        {
            await _proxyServer.CertificateManager.EnsureRootCertificateAsync();
        }
        else
        {
            _explicitEndPoint.GenericCertificate = await _proxyServer
                .CertificateManager
                .LoadRootCertificateAsync(stoppingToken);
        }

        _proxyServer.AddEndPoint(_explicitEndPoint);
        await _proxyServer.StartAsync();

        // run first-run setup on macOS
        FirstRunSetup();

        foreach (var endPoint in _proxyServer.ProxyEndPoints)
        {
            _logger.LogInformation("Dev Proxy listening on {ipAddress}:{port}...", endPoint.IpAddress, endPoint.Port);
        }

        if (_config.AsSystemProxy)
        {
            if (RunTime.IsWindows)
            {
                _proxyServer.SetAsSystemHttpProxy(_explicitEndPoint);
                _proxyServer.SetAsSystemHttpsProxy(_explicitEndPoint);
            }
            else if (RunTime.IsMac)
            {
                ToggleSystemProxy(ToggleSystemProxyAction.On, _config.IPAddress, _config.Port);
            }
            else
            {
                _logger.LogWarning("Configure your operating system to use this proxy's port and address {ipAddress}:{port}", _config.IPAddress, _config.Port);
            }
        }
        else
        {
            _logger.LogInformation("Configure your application to use this proxy's port and address");
        }

        var isInteractive = !Console.IsInputRedirected &&
            Environment.GetEnvironmentVariable("CI") is null;

        if (isInteractive)
        {
            // only print hotkeys when they can be used
            PrintHotkeys();
        }

        if (_config.Record)
        {
            StartRecording();
        }
        _pluginEvents.AfterRequestLog += AfterRequestLog;

        while (!stoppingToken.IsCancellationRequested && _proxyServer.ProxyRunning)
        {
            while (!Console.KeyAvailable)
            {
                await Task.Delay(10, stoppingToken);
            }
            // we need this check or proxy will fail with an exception
            // when run for example in VSCode's integrated terminal
            if (isInteractive)
            {
                ReadKeys();
            }
        }
    }

    private void FirstRunSetup()
    {
        if (!RunTime.IsMac ||
            _config.NoFirstRun ||
            !IsFirstRun() ||
            !_config.InstallCert)
        {
            return;
        }

        var bashScriptPath = Path.Join(ProxyUtils.AppFolder, "trust-cert.sh");
        ProcessStartInfo startInfo = new ProcessStartInfo()
        {
            FileName = "/bin/bash",
            Arguments = bashScriptPath,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        var process = new Process() { StartInfo = startInfo };
        process.Start();
        process.WaitForExit();
    }

    private bool IsFirstRun()
    {
        var firstRunFilePath = Path.Combine(ProxyUtils.AppFolder!, ".hasrun");
        if (File.Exists(firstRunFilePath))
        {
            return false;
        }

        try
        {
            File.WriteAllText(firstRunFilePath, "");
        }
        catch { }

        return true;
    }

    private void AfterRequestLog(object? sender, RequestLogArgs e)
    {
        if (!_proxyState.IsRecording)
        {
            return;
        }

        _proxyState.RequestLogs.Add(e.RequestLog);
    }

    private void ReadKeys()
    {
        var key = Console.ReadKey(true).Key;
        switch (key)
        {
            case ConsoleKey.R:
                StartRecording();
                break;
            case ConsoleKey.S:
                StopRecording().GetAwaiter().GetResult();
                break;
            case ConsoleKey.C:
                Console.Clear();
                PrintHotkeys();
                break;
            case ConsoleKey.W:
                _proxyState.RaiseMockRequest();
                break;
        }
    }

    private void StartRecording()
    {
        if (_proxyState.IsRecording)
        {
            return;
        }

        _proxyState.StartRecording();
    }

    private async Task StopRecording()
    {
        if (!_proxyState.IsRecording)
        {
            return;
        }

        await _proxyState.StopRecording();
    }

    // Convert strings from config to regexes.
    // From the list of URLs, extract host names and convert them to regexes.
    // We need this because before we decrypt a request, we only have access
    // to the host name, not the full URL.
    private void LoadHostNamesFromUrls()
    {
        foreach (var urlToWatch in _urlsToWatch)
        {
            // extract host from the URL
            string urlToWatchPattern = Regex.Unescape(urlToWatch.Url.ToString()).Replace(".*", "*");
            string hostToWatch;
            if (urlToWatchPattern.ToString().Contains("://"))
            {
                // if the URL contains a protocol, extract the host from the URL
                var urlChunks = urlToWatchPattern.Split("://");
                var slashPos = urlChunks[1].IndexOf("/");
                hostToWatch = slashPos < 0 ? urlChunks[1] : urlChunks[1].Substring(0, slashPos);
            }
            else
            {
                // if the URL doesn't contain a protocol,
                // we assume the whole URL is a host name
                hostToWatch = urlToWatchPattern;
            }

            // remove port number if present
            var portPos = hostToWatch.IndexOf(":");
            if (portPos > 0)
            {
                hostToWatch = hostToWatch.Substring(0, portPos);
            }

            var hostToWatchRegexString = Regex.Escape(hostToWatch).Replace("\\*", ".*");
            Regex hostRegex = new Regex($"^{hostToWatchRegexString}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // don't add the same host twice
            if (!_hostsToWatch.Any(h => h.Url.ToString() == hostRegex.ToString()))
            {
                _hostsToWatch.Add(new UrlToWatch(hostRegex));
            }
        }
    }

    private void StopProxy()
    {
        // Unsubscribe & Quit
        try
        {
            if (_explicitEndPoint != null)
            {
                _explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
            }

            if (_proxyServer is not null)
            {
                _proxyServer.BeforeRequest -= OnRequest;
                _proxyServer.BeforeResponse -= OnBeforeResponse;
                _proxyServer.AfterResponse -= OnAfterResponse;
                _proxyServer.ServerCertificateValidationCallback -= OnCertificateValidation;
                _proxyServer.ClientCertificateSelectionCallback -= OnCertificateSelection;

                _proxyServer.Stop();
            }

            if (RunTime.IsMac && _config.AsSystemProxy)
            {
                ToggleSystemProxy(ToggleSystemProxyAction.Off);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while stopping the proxy");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopRecording();
        StopProxy();

        await base.StopAsync(cancellationToken);
    }

    async Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e)
    {
        // Ensures that only the targeted Https domains are proxyied
        if (!IsProxiedHost(e.HttpClient.Request.RequestUri.Host) ||
            !IsProxiedProcess(e))
        {
            e.DecryptSsl = false;
        }
        await Task.CompletedTask;
    }

    private int GetProcessId(TunnelConnectSessionEventArgs e)
    {
        if (RunTime.IsWindows)
        {
            return e.HttpClient.ProcessId.Value;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "lsof",
            Arguments = $"-i :{e.ClientRemoteEndPoint?.Port}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        var proc = new Process
        {
            StartInfo = psi
        };
        proc.Start();
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var matchingLine = lines.FirstOrDefault(l => l.Contains($"{e.ClientRemoteEndPoint?.Port}->"));
        if (matchingLine is null)
        {
            return -1;
        }
        var pidString = Regex.Matches(matchingLine, @"^.*?\s+(\d+)")?.FirstOrDefault()?.Groups[1]?.Value;
        if (pidString is null)
        {
            return -1;
        }

        if (int.TryParse(pidString, out var pid))
        {
            return pid;
        }
        else
        {
            return -1;
        }
    }

    private bool IsProxiedProcess(TunnelConnectSessionEventArgs e)
    {
        // If no process names or IDs are specified, we proxy all processes
        if (!_config.WatchPids.Any() &&
            !_config.WatchProcessNames.Any())
        {
            return true;
        }

        var processId = GetProcessId(e);
        if (processId == -1)
        {
            return false;
        }

        if (_config.WatchPids.Any() &&
            _config.WatchPids.Contains(processId))
        {
            return true;
        }

        if (_config.WatchProcessNames.Any())
        {
            var processName = Process.GetProcessById(processId).ProcessName;
            if (_config.WatchProcessNames.Contains(processName))
            {
                return true;
            }
        }

        return false;
    }

    async Task OnRequest(object sender, SessionEventArgs e)
    {
        if (IsProxiedHost(e.HttpClient.Request.RequestUri.Host) &&
            IsIncludedByHeaders(e.HttpClient.Request.Headers))
        {
            _pluginData.Add(e.GetHashCode(), []);
            var responseState = new ResponseState();
            var proxyRequestArgs = new ProxyRequestArgs(e, responseState)
            {
                SessionData = _pluginData[e.GetHashCode()],
                GlobalData = _proxyState.GlobalData
            };
            if (!proxyRequestArgs.HasRequestUrlMatch(_urlsToWatch))
            {
                return;
            }


            // we need to keep the request body for further processing
            // by plugins
            e.HttpClient.Request.KeepBody = true;
            if (e.HttpClient.Request.HasBody)
            {
                await e.GetRequestBodyAsString();
            }

            using var scope = _logger.BeginScope(e.HttpClient.Request.Method ?? "", e.HttpClient.Request.Url, e.GetHashCode());

            e.UserData = e.HttpClient.Request;
            _logger.LogRequest(new[] { $"{e.HttpClient.Request.Method} {e.HttpClient.Request.Url}" }, MessageType.InterceptedRequest, new LoggingContext(e));
            await HandleRequest(e, proxyRequestArgs);
        }
    }

    private async Task HandleRequest(SessionEventArgs e, ProxyRequestArgs proxyRequestArgs)
    {
        await _pluginEvents.RaiseProxyBeforeRequest(proxyRequestArgs, _exceptionHandler);

        // We only need to set the proxy header if the proxy has not set a response and the request is going to be sent to the target.
        if (!proxyRequestArgs.ResponseState.HasBeenSet)
        {
            _logger?.LogRequest(["Passed through"], MessageType.PassedThrough, new LoggingContext(e));
            AddProxyHeader(e.HttpClient.Request);
        }
    }

    private static void AddProxyHeader(Request r) => r.Headers?.AddHeader("Via", $"{r.HttpVersion} dev-proxy/{ProxyUtils.ProductVersion}");

    private bool IsProxiedHost(string hostName) => _hostsToWatch.Any(h => h.Url.IsMatch(hostName));

    private bool IsIncludedByHeaders(HeaderCollection requestHeaders)
    {
        if (_config.FilterByHeaders is null)
        {
            return true;
        }

        foreach (var header in _config.FilterByHeaders)
        {
            _logger.LogDebug("Checking header {header} with value {value}...",
                header.Name,
                string.IsNullOrEmpty(header.Value) ? "(any)" : header.Value
            );

            if (requestHeaders.HeaderExists(header.Name))
            {
                if (string.IsNullOrEmpty(header.Value))
                {
                    _logger.LogDebug("Request has header {header}", header.Name);
                    return true;
                }

                if (requestHeaders.GetHeaders(header.Name)!.Any(h => h.Value.Contains(header.Value)))
                {
                    _logger.LogDebug("Request header {header} contains value {value}", header.Name, header.Value);
                    return true;
                }
            }
            else
            {
                _logger.LogDebug("Request doesn't have header {header}", header.Name);
            }
        }

        _logger.LogDebug("Request doesn't match any header filter. Ignoring");
        return false;
    }

    // Modify response
    async Task OnBeforeResponse(object sender, SessionEventArgs e)
    {
        // read response headers
        if (IsProxiedHost(e.HttpClient.Request.RequestUri.Host))
        {
            var proxyResponseArgs = new ProxyResponseArgs(e, new ResponseState())
            {
                SessionData = _pluginData[e.GetHashCode()],
                GlobalData = _proxyState.GlobalData
            };
            if (!proxyResponseArgs.HasRequestUrlMatch(_urlsToWatch))
            {
                return;
            }

            using var scope = _logger.BeginScope(e.HttpClient.Request.Method ?? "", e.HttpClient.Request.Url, e.GetHashCode());

            // necessary to make the response body available to plugins
            e.HttpClient.Response.KeepBody = true;
            if (e.HttpClient.Response.HasBody)
            {
                await e.GetResponseBody();
            }

            await _pluginEvents.RaiseProxyBeforeResponse(proxyResponseArgs, _exceptionHandler);
        }
    }
    async Task OnAfterResponse(object sender, SessionEventArgs e)
    {
        // read response headers
        if (IsProxiedHost(e.HttpClient.Request.RequestUri.Host))
        {
            var proxyResponseArgs = new ProxyResponseArgs(e, new ResponseState())
            {
                SessionData = _pluginData[e.GetHashCode()],
                GlobalData = _proxyState.GlobalData
            };
            if (!proxyResponseArgs.HasRequestUrlMatch(_urlsToWatch))
            {
                // clean up
                _pluginData.Remove(e.GetHashCode());
                return;
            }

            // necessary to repeat to make the response body
            // of mocked requests available to plugins
            e.HttpClient.Response.KeepBody = true;

            using var scope = _logger.BeginScope(e.HttpClient.Request.Method ?? "", e.HttpClient.Request.Url, e.GetHashCode());

            var message = $"{e.HttpClient.Request.Method} {e.HttpClient.Request.Url}";
            _logger.LogRequest([message], MessageType.InterceptedResponse, new LoggingContext(e));
            await _pluginEvents.RaiseProxyAfterResponse(proxyResponseArgs, _exceptionHandler);
            _logger.LogRequest([message], MessageType.FinishedProcessingRequest, new LoggingContext(e));

            // clean up
            _pluginData.Remove(e.GetHashCode());
        }
    }

    // Allows overriding default certificate validation logic
    Task OnCertificateValidation(object sender, CertificateValidationEventArgs e)
    {
        // set IsValid to true/false based on Certificate Errors
        if (e.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
        {
            e.IsValid = true;
        }

        return Task.CompletedTask;
    }

    // Allows overriding default client certificate selection logic during mutual authentication
    Task OnCertificateSelection(object sender, CertificateSelectionEventArgs e)
    {
        // set e.clientCertificate to override
        return Task.CompletedTask;
    }

    private void PrintHotkeys()
    {
        Console.WriteLine("");
        Console.WriteLine("Hotkeys: issue (w)eb request, (r)ecord, (s)top recording, (c)lear screen");
        Console.WriteLine("Press CTRL+C to stop Dev Proxy");
        Console.WriteLine("");
    }
}
