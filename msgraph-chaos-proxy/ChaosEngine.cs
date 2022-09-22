using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.Graph.ChaosProxy {
    internal enum FailMode {
        Throttled,
        Random,
        PassThru
    }

    public class ChaosEngine {
        private int retryAfterInSeconds = 5;
        private readonly Dictionary<string, HttpStatusCode[]> _methodStatusCode = new()
        {
            {
                "GET", new[] {
                    HttpStatusCode.TooManyRequests,
                    HttpStatusCode.InternalServerError,
                    HttpStatusCode.BadGateway,
                    HttpStatusCode.ServiceUnavailable,
                    HttpStatusCode.GatewayTimeout
                }
            },
            {
                "POST", new[] {
                    HttpStatusCode.TooManyRequests,
                    HttpStatusCode.InternalServerError,
                    HttpStatusCode.BadGateway,
                    HttpStatusCode.ServiceUnavailable,
                    HttpStatusCode.GatewayTimeout,
                    HttpStatusCode.InsufficientStorage
                }
            },
            {
                "PUT", new[] {
                    HttpStatusCode.TooManyRequests,
                    HttpStatusCode.InternalServerError,
                    HttpStatusCode.BadGateway,
                    HttpStatusCode.ServiceUnavailable,
                    HttpStatusCode.GatewayTimeout,
                    HttpStatusCode.InsufficientStorage
                }
            },
            {
                "PATCH", new[] {
                    HttpStatusCode.TooManyRequests,
                    HttpStatusCode.InternalServerError,
                    HttpStatusCode.BadGateway,
                    HttpStatusCode.ServiceUnavailable,
                    HttpStatusCode.GatewayTimeout
                }
            },
            {
                "DELETE", new[] {
                    HttpStatusCode.TooManyRequests,
                    HttpStatusCode.InternalServerError,
                    HttpStatusCode.BadGateway,
                    HttpStatusCode.ServiceUnavailable,
                    HttpStatusCode.GatewayTimeout,
                    HttpStatusCode.InsufficientStorage
                }
            }
        };

        private readonly ChaosProxyConfiguration _config;
        private readonly Random _random;
        private ProxyServer? _proxyServer;
        private ExplicitProxyEndPoint? _explicitEndPoint;
        private readonly Dictionary<string, DateTime> _throttledRequests;
        private readonly ConsoleColor _color;

        public ChaosEngine(ChaosProxyConfiguration config) {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.InitResponsesWatcher();

            _random = new Random();
            _throttledRequests = new Dictionary<string, DateTime>();
            if (_config.AllowedErrors.Any()) {
                foreach (string k in _methodStatusCode.Keys) {
                    _methodStatusCode[k] = _methodStatusCode[k].Where(e => _config.AllowedErrors.Any(a => (int)e == a)).ToArray();
                }
            }

            _color = Console.ForegroundColor;
        }

        public async Task Run(CancellationToken? cancellationToken) {
            Console.WriteLine($"Configuring proxy for cloud {_config.Cloud} - {_config.HostName}");
            _proxyServer = new ProxyServer();

            _proxyServer.BeforeRequest += OnRequest;
            _proxyServer.BeforeResponse += OnResponse;
            _proxyServer.ServerCertificateValidationCallback += OnCertificateValidation;
            _proxyServer.ClientCertificateSelectionCallback += OnCertificateSelection;
            cancellationToken?.Register(OnCancellation);

            _explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Any, _config.Port, true) {
                // Use self-issued generic certificate on all https requests
                // Optimizes performance by not creating a certificate for each https-enabled domain
                // Useful when certificate trust is not required by proxy clients
                //GenericCertificate = new X509Certificate2(Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "genericcert.pfx"), "password")
            };

            // Fired when a CONNECT request is received
            _explicitEndPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequest;

            _proxyServer.AddEndPoint(_explicitEndPoint);
            _proxyServer.Start();

            foreach (var endPoint in _proxyServer.ProxyEndPoints) {
                Console.WriteLine("Listening on '{0}' endpoint at Ip {1} and port: {2} ",
                    endPoint.GetType().Name, endPoint.IpAddress, endPoint.Port);
            }

            // Only explicit proxies can be set as system proxy!
            _proxyServer.SetAsSystemHttpProxy(_explicitEndPoint);
            _proxyServer.SetAsSystemHttpsProxy(_explicitEndPoint);

            // wait here (You can use something else as a wait function, I am using this as a demo)
            Console.WriteLine("Press Enter to stop the Microsoft Graph Chaos Proxy");
            Console.ReadLine();

            // Unsubscribe & Quit
            _explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
            _proxyServer.BeforeRequest -= OnRequest;
            _proxyServer.BeforeResponse -= OnResponse;
            _proxyServer.ServerCertificateValidationCallback -= OnCertificateValidation;
            _proxyServer.ClientCertificateSelectionCallback -= OnCertificateSelection;

            _proxyServer.Stop();
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

        // uses config to determine if a request should be failed
        private FailMode ShouldFail(Request r) {
            string key = BuildThrottleKey(r);
            if (_throttledRequests.TryGetValue(key, out DateTime retryAfterDate)) {
                if (retryAfterDate > DateTime.Now) {
                    Console.Error.WriteLine($"Calling {r.Url} again before waiting for the Retry-After period. Request will be throttled");
                    // update the retryAfterDate to extend the throttling window to ensure that brute forcing won't succeed.
                    _throttledRequests[key] = retryAfterDate.AddSeconds(retryAfterInSeconds);
                    return FailMode.Throttled;
                }
                else {
                    // clean up expired throttled request and ensure that this request is passed through.
                    _throttledRequests.Remove(key);
                    return FailMode.PassThru;
                }
            }

            return _random.Next(1, 100) <= _config.FailureRate ? FailMode.Random : FailMode.PassThru;
        }

        async Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e) {
            string hostname = e.HttpClient.Request.RequestUri.Host;

            // Ensures that only the targeted Https domains are proxyied
            if (!hostname.Contains(_config.HostName)) {
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
            if (method is not "OPTIONS" && e.HttpClient.Request.RequestUri.Host.Contains(_config.HostName)) {
                Console.WriteLine($"saw a graph request: {e.HttpClient.Request.Method} {e.HttpClient.Request.RequestUri.AbsolutePath}");
                HandleGraphRequest(e);
            }
        }

        private void HandleGraphRequest(SessionEventArgs e) {
            var responseComponents = ResponseComponents.Build();
            var matchingResponse = GetMatchingMockResponse(e.HttpClient.Request);
            if (matchingResponse is not null) {
                ProcessMockResponse(e, responseComponents, matchingResponse);
            }
            else {
                var failMode = ShouldFail(e.HttpClient.Request);
                if (failMode == FailMode.PassThru && _config.FailureRate != 100) {
                    Console.WriteLine($"\tPassed through {e.HttpClient.Request.RequestUri.AbsolutePath}");
                    return;
                }

                FailResponse(e, responseComponents, failMode);
                if (!IsSdkRequest(e.HttpClient.Request)) {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Error.WriteLine($"\tTIP: {BuildUseSdkMessage(e.HttpClient.Request)}");
                    Console.ForegroundColor = _color;
                }
            }
            if (!responseComponents.ResponseIsComplete)
                UpdateProxyResponse(e, responseComponents, matchingResponse);
        }

        private static string BuildUseSdkMessage(Request r) => $"To handle API errors more easily, use the Graph SDK. More info at {GetMoveToSdkUrl(r)}";

        private void FailResponse(SessionEventArgs e, ResponseComponents r, FailMode failMode) {
            if (failMode == FailMode.Throttled) {
                r.ErrorStatus = HttpStatusCode.TooManyRequests;
            }
            else {
                // there's no matching mock response so pick a random response
                // for the current request method
                var methodStatusCodes = _methodStatusCode[e.HttpClient.Request.Method];
                r.ErrorStatus = methodStatusCodes[_random.Next(0, methodStatusCodes.Length)];
            }
        }

        private static bool IsSdkRequest(Request request) {
            return request.Headers.HeaderExists("SdkVersion");
        }

        private static string GetMoveToSdkUrl(Request request) {
            // TODO: return language-specific guidance links based on the language detected from the User-Agent
            return "https://aka.ms/move-to-graph-js-sdk";
        }

        private static void ProcessMockResponse(SessionEventArgs e, ResponseComponents responseComponents, ChaosProxyMockResponse matchingResponse) {
            if (matchingResponse.ResponseCode is not null) {
                responseComponents.ErrorStatus = (HttpStatusCode)matchingResponse.ResponseCode;
            }

            if (matchingResponse.ResponseHeaders is not null) {
                foreach (var key in matchingResponse.ResponseHeaders.Keys) {
                    responseComponents.Headers.Add(new HttpHeader(key, matchingResponse.ResponseHeaders[key]));
                }
            }

            if (matchingResponse.ResponseBody is not null) {
                var bodyString = JsonSerializer.Serialize(matchingResponse.ResponseBody) as string;
                // we get a JSON string so need to start with the opening quote
                if (bodyString?.StartsWith("\"@") ?? false) {
                    // we've got a mock body starting with @-token which means we're sending
                    // a response from a file on disk
                    // if we can read the file, we can immediately send the response and
                    // skip the rest of the logic in this method
                    // remove the surrounding quotes and the @-token
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), bodyString.Trim('"').Substring(1));
                    if (!File.Exists(filePath)) {
                        Console.Error.WriteLine($"File {filePath} not found. Serving file path in the mock response");
                        responseComponents.Body = bodyString;
                    }
                    else {
                        if (e.HttpClient.Request.Headers.FirstOrDefault((HttpHeader h) => h.Name.Equals("Origin", StringComparison.OrdinalIgnoreCase)) is not null) {
                            responseComponents.Headers.Add(new HttpHeader("Access-Control-Allow-Origin", "*"));
                        }

                        var bodyBytes = File.ReadAllBytes(filePath);
                        e.GenericResponse(bodyBytes, responseComponents.ErrorStatus, responseComponents.Headers);
                        responseComponents.ResponseIsComplete = true;
                    }
                }
                else {
                    responseComponents.Body = bodyString;
                }
            }
        }

        private ChaosProxyMockResponse? GetMatchingMockResponse(Request request) {
            if (_config.NoMocks ||
                _config.Responses is null ||
                !_config.Responses.Any()) {
                return null;
            }

            var mockResponse = _config.Responses.FirstOrDefault(r => {
                if (r.Method != request.Method) return false;
                if (r.Url == request.RequestUri.AbsolutePath) {
                    return true;
                }

                // check if the URL contains a wildcard
                // if it doesn't, it's not a match for the current request for sure
                if (!r.Url.Contains('*')) {
                    return false;
                }

                // turn mock URL with wildcard into a regex and match against the request URL
                var urlRegex = Regex.Escape(r.Url).Replace("\\*", ".*");
                return Regex.IsMatch(request.RequestUri.AbsolutePath, urlRegex);
            });
            return mockResponse;
        }

        private void UpdateProxyResponse(SessionEventArgs e, ResponseComponents responseComponents, ChaosProxyMockResponse? matchingResponse) {
            if (responseComponents.ErrorStatus == HttpStatusCode.TooManyRequests) {
                var retryAfterDate = DateTime.Now.AddSeconds(retryAfterInSeconds);
                _throttledRequests[BuildThrottleKey(e.HttpClient.Request)] = retryAfterDate;
                responseComponents.Headers.Add(new HttpHeader("Retry-After", retryAfterInSeconds.ToString()));
            }

            if (e.HttpClient.Request.Headers.FirstOrDefault((HttpHeader h) => h.Name.Equals("Origin", StringComparison.OrdinalIgnoreCase)) is not null) {
                responseComponents.Headers.Add(new HttpHeader("Access-Control-Allow-Origin", "*"));
            }

            if ((int)responseComponents.ErrorStatus >= 400 && string.IsNullOrEmpty(responseComponents.Body)) {
                responseComponents.Body = JsonSerializer.Serialize(new ErrorResponseBody(
                    new ErrorResponseError {
                        Code = new Regex("([A-Z])").Replace(responseComponents.ErrorStatus.ToString(), m => { return $" {m.Groups[1]}"; }).Trim(),
                        Message = BuildApiErrorMessage(e.HttpClient.Request),
                        InnerError = new ErrorResponseInnerError {
                            RequestId = responseComponents.RequestId,
                            Date = responseComponents.RequestDate
                        }
                    })
                );
            }
            Console.WriteLine($"\t{(matchingResponse is not null ? "Mocked" : "Failed")} {e.HttpClient.Request.RequestUri.AbsolutePath} with {responseComponents.ErrorStatus}");
            e.GenericResponse(responseComponents.Body ?? string.Empty, responseComponents.ErrorStatus, responseComponents.Headers);
        }

        private string BuildApiErrorMessage(Request r) => $"Some error was generated by the proxy. {(IsSdkRequest(r) ? "" : BuildUseSdkMessage(r))}";  

        private string BuildThrottleKey(Request r) => $"{r.Method}-{r.Url}";

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

    public class ResponseComponents {
        public string RequestId { get; } = Guid.NewGuid().ToString();
        public string RequestDate { get; } = DateTime.Now.ToString();
        public List<HttpHeader> Headers { get; } = new List<HttpHeader>
        {
            new HttpHeader("Cache-Control", "no-store"),
            new HttpHeader("x-ms-ags-diagnostic", ""),
            new HttpHeader("Strict-Transport-Security", "")
        };

        public string? Body { get; set; } = string.Empty;
        public HttpStatusCode ErrorStatus { get; set; } = HttpStatusCode.OK;
        public bool ResponseIsComplete { get; set; } = false;

        public static ResponseComponents Build() {
            var result = new ResponseComponents();
            result.Headers.Add(new HttpHeader("request-id", result.RequestId));
            result.Headers.Add(new HttpHeader("client-request-id", result.RequestId));
            result.Headers.Add(new HttpHeader("Date", result.RequestDate));
            return result;
        }
    }
}