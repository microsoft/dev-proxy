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

        public ChaosEngine(ChaosProxyConfiguration config) {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.InitResponsesWatcher();

            _random = new Random();
            _throttledRequests = new Dictionary<string, DateTime>();
        }

        public async Task Run(CancellationToken? cancellationToken) {
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
            DateTime retryAfterDate = DateTime.MaxValue;
            if (_throttledRequests.TryGetValue(r.Url, out retryAfterDate)) {
                if (retryAfterDate > DateTime.Now) {
                    Console.Error.WriteLine($"Calling {r.Url} again before waiting for the Retry-After period. Request will be throttled");
                    return FailMode.Throttled;
                }
                else {
                    // clean up expired throttled request
                    _throttledRequests.Remove(r.Url);
                }
            }

            return _random.Next(1, 100) <= _config.FailureRate ? FailMode.Random : FailMode.PassThru;
        }

        async Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e) {
            string hostname = e.HttpClient.Request.RequestUri.Host;

            // TODO: Provide host name to be watched via config based on host name for the desired cloud
            if (!hostname.Contains(_config.HostName)) {
                // Exclude Https addresses you don't want to proxy
                e.DecryptSsl = false;
            }
        }

        async Task OnRequest(object sender, SessionEventArgs e) {
            // read request headers
            var requestHeaders = e.HttpClient.Request.Headers;

            var method = e.HttpClient.Request.Method.ToUpper();
            if ((method == "POST" || method == "PUT" || method == "PATCH")) {
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

            // Chaos happens only for graph requestss
            if (e.HttpClient.Request.RequestUri.Host.Contains(_config.HostName)) {
                Console.WriteLine($"saw a graph request: {e.HttpClient.Request.Method} {e.HttpClient.Request.RequestUri.AbsolutePath}");
                var failMode = ShouldFail(e.HttpClient.Request);
                if (failMode != FailMode.PassThru) {
                    FailResponse(e, failMode);
                }
            }
        }

        private void FailResponse(SessionEventArgs e, FailMode failMode) {
            var requestId = Guid.NewGuid().ToString();
            var requestDate = DateTime.Now.ToString();
            var headers = new List<HttpHeader> {
                new HttpHeader("Cache-Control", "no-store"),
                new HttpHeader("request-id", requestId),
                new HttpHeader("client-request-id", requestId),
                new HttpHeader("x-ms-ags-diagnostic", ""),
                new HttpHeader("Date", requestDate),
                new HttpHeader("Strict-Transport-Security", "")
            };

            var body = "";
            var errorStatus = HttpStatusCode.OK;

            var matchingResponse = GetMatchingMockResponse(e.HttpClient.Request);
            if (matchingResponse is not null) {
                if (matchingResponse.ResponseCode is not null) {
                    errorStatus = (HttpStatusCode)matchingResponse.ResponseCode;
                }

                if (matchingResponse.ResponseHeaders is not null) {
                    foreach (var key in matchingResponse.ResponseHeaders.Keys) {
                        headers.Add(new HttpHeader(key, matchingResponse.ResponseHeaders[key]));
                    }
                }

                if (!(matchingResponse.ResponseBody is null)) {
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
                            body = bodyString;
                        }
                        else {
                            var bodyBytes = File.ReadAllBytes(filePath);
                            e.GenericResponse(bodyBytes, errorStatus, headers);
                            return;
                        }
                    }
                    else {
                        body = bodyString;
                    }
                }
            }
            else {
                if (failMode == FailMode.Throttled) {
                    errorStatus = HttpStatusCode.TooManyRequests;
                }
                else {
                    // there's no matching mock response so pick a random response
                    // for the current request method
                    var methodStatusCodes = _methodStatusCode[e.HttpClient.Request.Method];
                    errorStatus = methodStatusCodes[_random.Next(0, methodStatusCodes.Length - 1)];
                }
            }

            if (errorStatus == HttpStatusCode.TooManyRequests) {
                var retryAfterDate = DateTime.Now.AddSeconds(retryAfterInSeconds);
                _throttledRequests[e.HttpClient.Request.Url] = retryAfterDate;
                headers.Add(new HttpHeader("Retry-After", retryAfterInSeconds.ToString()));
            }

            if ((int)errorStatus >= 400 && string.IsNullOrEmpty(body)) {
                body = JsonSerializer.Serialize(new ErrorResponseBody {
                    Error = new ErrorResponseError {
                        Code = new Regex("([A-Z])").Replace(errorStatus.ToString(), m => { return $" {m.Groups[1]}"; }).Trim(),
                        Message = "Some error happened",
                        InnerError = new ErrorResponseInnerError {
                            RequestId = requestId,
                            Date = requestDate
                        }
                    }
                });
            }
            Console.WriteLine($"\t {(matchingResponse is not null ? "Mocked" : "Failed")} {e.HttpClient.Request.RequestUri.AbsolutePath} with {errorStatus}");
            e.GenericResponse(body ?? string.Empty, errorStatus, headers);
        }

        private ChaosProxyMockResponse? GetMatchingMockResponse(Request request) {
            if (_config.NoMocks ||
                _config.Responses is null ||
                !_config.Responses.Any()) {
                return null;
            }

            var mockResponse = _config.Responses.FirstOrDefault(r => {
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

        // Modify response
        async Task OnResponse(object sender, SessionEventArgs e) {
            // read response headers
            var responseHeaders = e.HttpClient.Response.Headers;

            if (e.HttpClient.Request.Method == "GET" || e.HttpClient.Request.Method == "POST") {
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