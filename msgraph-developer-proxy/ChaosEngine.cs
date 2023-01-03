// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Helpers;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.Graph.DeveloperProxy {

    public class ChaosEngine {

        private readonly PluginEvents _pluginEvents;
        private readonly ILogger _logger;
        private readonly ProxyConfiguration _config;
        private ProxyServer? _proxyServer;
        private ExplicitProxyEndPoint? _explicitEndPoint;
        private readonly ConsoleColor _color;
        // lists of URLs to watch, used for intercepting requests
        private ISet<Regex> _urlsToWatch = new HashSet<Regex>();
        // lists of hosts to watch extracted from urlsToWatch,
        // used for deciding which URLs to decrypt for further inspection
        private ISet<Regex> _hostsToWatch = new HashSet<Regex>();


        private static string __productVersion = string.Empty;
        private static string _productVersion {
            get {
                if (__productVersion == string.Empty) {
                    var assembly = Assembly.GetExecutingAssembly();
                    if (assembly != null) {
                        var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                        __productVersion = fileVersionInfo?.ProductVersion!;
                    }
                }

                return __productVersion;
            }
        }

        public ChaosEngine(ProxyConfiguration config, ISet<Regex> urlsToWatch, PluginEvents pluginEvents, ILogger logger) {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.InitResponsesWatcher();

            _color = Console.ForegroundColor;
            _urlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
            _pluginEvents = pluginEvents ?? throw new ArgumentNullException(nameof(pluginEvents));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Run(CancellationToken? cancellationToken) {
            if (!_urlsToWatch.Any()) {
                Console.WriteLine("No URLs to watch configured. Please add URLs to watch in the appsettings.json config file.");
                return;
            }

            LoadHostNamesFromUrls();

            _proxyServer = new ProxyServer();

            _proxyServer.CertificateManager.CertificateStorage = new CertificateDiskCache();
            _proxyServer.BeforeRequest += OnRequest;
            _proxyServer.BeforeResponse += OnResponse;
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
                Console.WriteLine("Listening on '{0}' endpoint at Ip {1} and port: {2} ",
                    endPoint.GetType().Name, endPoint.IpAddress, endPoint.Port);
            }

            if (RunTime.IsWindows) {
                // Only explicit proxies can be set as system proxy!
                _proxyServer.SetAsSystemHttpProxy(_explicitEndPoint);
                _proxyServer.SetAsSystemHttpsProxy(_explicitEndPoint);
            }
            else {
                var color = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Configure your operating system to use this proxy's port and address");
                Console.ForegroundColor = color;
            }

            // wait here (You can use something else as a wait function, I am using this as a demo)
            Console.WriteLine("Press CTRL+C to stop the Microsoft Graph Developer Proxy");
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

                if (_proxyServer != null) {
                    _proxyServer.BeforeRequest -= OnRequest;
                    _proxyServer.BeforeResponse -= OnResponse;
                    _proxyServer.ServerCertificateValidationCallback -= OnCertificateValidation;
                    _proxyServer.ClientCertificateSelectionCallback -= OnCertificateSelection;

                    _proxyServer.Stop();
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Exception: {ex.Message}");
            }
        }

        private void OnCancellation() {
            if (_explicitEndPoint is not null) {
                _explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
            }

            if (_proxyServer is not null) {
                _proxyServer.BeforeRequest -= OnRequest;
                _proxyServer.BeforeResponse -= OnResponse;
                _proxyServer.ServerCertificateValidationCallback -= OnCertificateValidation;
                _proxyServer.ClientCertificateSelectionCallback -= OnCertificateSelection;

                _proxyServer.Stop();
            }
        }

        async Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e) {
            // Ensures that only the targeted Https domains are proxyied
            if (!ShouldDecryptRequest(e.HttpClient.Request.RequestUri.Host)) {
                e.DecryptSsl = false;
            }
        }

        async Task OnRequest(object sender, SessionEventArgs e) {
            var method = e.HttpClient.Request.Method.ToUpper();
            if (method is "POST" or "PUT" or "PATCH") {
                // Get/Set request body bytes
                byte[] bodyBytes = await e.GetRequestBody();
                e.SetRequestBody(bodyBytes);

                // Get/Set request body as string
                string bodyString = await e.GetRequestBodyAsString();
                e.SetRequestBodyString(bodyString);

                // store request
                // so that you can find it from response handler
                e.UserData = e.HttpClient.Request;
            }

            // Chaos happens only for graph requests which are not OPTIONS
            if (method is not "OPTIONS") {
                Console.WriteLine($"saw a graph request: {e.HttpClient.Request.Method} {e.HttpClient.Request.RequestUriString}");
                HandleGraphRequest(e);
            }
        }

        private void HandleGraphRequest(SessionEventArgs e) {
            ResponseState responseState = new ResponseState();
            _pluginEvents.FireProxyRequest(new ProxyRequestArgs(e, responseState));

            //var responseComponents = ResponseComponents.Build();
            // internal array of request handlers
            //foreach(var h in this._handlers)
            //    h.HandleRequest(e, responseComponents);

            //var matchingResponse = GetMatchingMockResponse(e.HttpClient.Request);
            //if (matchingResponse is not null) {
            //    ProcessMockResponse(e, responseComponents, matchingResponse);
            //}
            //else {

            if (ProxyUtils.IsGraphRequest(e.HttpClient.Request) &&
                !ProxyUtils.IsSdkRequest(e.HttpClient.Request)) {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Error.WriteLine($"\tTIP: {BuildUseSdkMessage(e.HttpClient.Request)}");
                Console.ForegroundColor = _color;
            }
            // We only need to set the proxy header if the proxy has not set a response and the request is going to be sent to the target.
            if (!responseState.HasBeenSet) {
                AddProxyHeader(e.HttpClient.Request);
            }
        }

        private static void AddProxyHeader(Request r) {
            if (r.Headers is not null) {
                r.Headers.AddHeader("Via", $"{r.HttpVersion} graph-proxy/{_productVersion}");
            }
        }

        //private static void ProcessMockResponse(SessionEventArgs e, ResponseComponents responseComponents, ProxyMockResponse matchingResponse) {
        //    if (matchingResponse.ResponseCode is not null) {
        //        responseComponents.ErrorStatus = (HttpStatusCode)matchingResponse.ResponseCode;
        //    }

        //    if (matchingResponse.ResponseHeaders is not null) {
        //        foreach (var key in matchingResponse.ResponseHeaders.Keys) {
        //            responseComponents.Headers.Add(new HttpHeader(key, matchingResponse.ResponseHeaders[key]));
        //        }
        //    }

        //    if (matchingResponse.ResponseBody is not null) {
        //        var bodyString = JsonSerializer.Serialize(matchingResponse.ResponseBody) as string;
        //        // we get a JSON string so need to start with the opening quote
        //        if (bodyString?.StartsWith("\"@") ?? false) {
        //            // we've got a mock body starting with @-token which means we're sending
        //            // a response from a file on disk
        //            // if we can read the file, we can immediately send the response and
        //            // skip the rest of the logic in this method
        //            // remove the surrounding quotes and the @-token
        //            var filePath = Path.Combine(Directory.GetCurrentDirectory(), bodyString.Trim('"').Substring(1));
        //            if (!File.Exists(filePath)) {
        //                Console.Error.WriteLine($"File {filePath} not found. Serving file path in the mock response");
        //                responseComponents.Body = bodyString;
        //            }
        //            else {
        //                if (e.HttpClient.Request.Headers.FirstOrDefault((HttpHeader h) => h.Name.Equals("Origin", StringComparison.OrdinalIgnoreCase)) is not null) {
        //                    responseComponents.Headers.Add(new HttpHeader("Access-Control-Allow-Origin", "*"));
        //                }

        //                var bodyBytes = File.ReadAllBytes(filePath);
        //                e.GenericResponse(bodyBytes, responseComponents.ErrorStatus, responseComponents.Headers);
        //                responseComponents.ResponseIsComplete = true;
        //            }
        //        }
        //        else {
        //            responseComponents.Body = bodyString;
        //        }
        //    }
        //}

        private bool ShouldDecryptRequest(string hostName) {
            return _hostsToWatch.Any(h => h.IsMatch(hostName));
        }

        private bool ShouldWatchRequest(string requestUrl) {
            return _urlsToWatch.Any(u => u.IsMatch(requestUrl));
        }

        //private ProxyMockResponse? GetMatchingMockResponse(Request request) {
        //    if (_config.NoMocks ||
        //        _config.Responses is null ||
        //        !_config.Responses.Any()) {
        //        return null;
        //    }

        //    var mockResponse = _config.Responses.FirstOrDefault(mockResponse => {
        //        if (mockResponse.Method != request.Method) return false;
        //        if (mockResponse.Url == request.Url) {
        //            return true;
        //        }

        //        check if the URL contains a wildcard
        //         if it doesn't, it's not a match for the current request for sure
        //        if (!mockResponse.Url.Contains('*')) {
        //                    return false;
        //                }

        //        turn mock URL with wildcard into a regex and match against the request URL
        //        var mockResponseUrlRegex = Regex.Escape(mockResponse.Url).Replace("\\*", ".*");
        //        return Regex.IsMatch(request.Url, mockResponseUrlRegex);
        //    });
        //    return mockResponse;
        //}

        // Modify response
        async Task OnResponse(object sender, SessionEventArgs e) {
            // read response headers
            var responseHeaders = e.HttpClient.Response.Headers;

            if (e.HttpClient.Request.Method is "GET" or "POST") {
                if (e.HttpClient.Response.StatusCode == 200) {
                    if (e.HttpClient.Response.ContentType is not null && e.HttpClient.Response.ContentType.Trim().ToLower().Contains("text/html")) {
                        byte[] bodyBytes = await e.GetResponseBody();
                        e.SetResponseBody(bodyBytes);

                        string body = await e.GetResponseBodyAsString();
                        e.SetResponseBodyString(body);
                    }
                }
            }

            if (e.UserData is not null) {
                // access request from UserData property where we stored it in RequestHandler
                var request = (Request)e.UserData;
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
}