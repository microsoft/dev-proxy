using System.Net;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.Graph.ChaosProxy {
    public class ChaosEngine {
        private readonly ChaosProxyConfiguration _config;
        private readonly Random _random;
        private ProxyServer? _proxyServer;
        private ExplicitProxyEndPoint? _explicitEndPoint;

        public ChaosEngine(ChaosProxyConfiguration config) {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _random = new Random();
        }

        public async Task Run(CancellationToken cancellationToken) {
            _proxyServer = new ProxyServer();

            _proxyServer.BeforeRequest += OnRequest;
            _proxyServer.BeforeResponse += OnResponse;
            _proxyServer.ServerCertificateValidationCallback += OnCertificateValidation;
            _proxyServer.ClientCertificateSelectionCallback += OnCertificateSelection;
            cancellationToken.Register(OnCancellation);

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
        private bool ShouldFail() => _random.Next(1, 100) <= _config.FailureRate;

        async Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e) {
            string hostname = e.HttpClient.Request.RequestUri.Host;

            // TODO: Provide host name to be watched via config based on host name for the desired cloud
            if (!hostname.Contains("graph.microsoft.com")) {
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
            if (e.HttpClient.Request.RequestUri.AbsoluteUri.Contains("graph.microsoft.com")) {
                Console.WriteLine($"saw a graph request: {e.HttpClient.Request.Method} {e.HttpClient.Request.RequestUri.AbsolutePath}");
                if (ShouldFail()) {
                    FailResponse(e);
                }
            }
        }

        // Hacky implementation to provide randomized failures
        // Skews toward providing 429 
        // Additional headers should be added to more accurately represent the failure
        // Retry after times should be pseudo randomized?
        private void FailResponse(SessionEventArgs e) {
            int wheel = _random.Next(1, 10);
            switch (wheel) {
                case 1:
                    e.GenericResponse("", HttpStatusCode.InternalServerError, new List<HttpHeader>() { });
                    return;
                case 2:
                    e.GenericResponse("", HttpStatusCode.BadGateway, new List<HttpHeader>() { });
                    return;
                case 3:
                    e.GenericResponse("", HttpStatusCode.ServiceUnavailable, new List<HttpHeader>() { });
                    return;
                case 4:
                    e.GenericResponse("", HttpStatusCode.GatewayTimeout, new List<HttpHeader>() { });
                    return;
                default:
                    e.GenericResponse("", HttpStatusCode.TooManyRequests, new List<HttpHeader>() { new HttpHeader("Retry-After", "12") });
                    return;
            }
        }

        // Modify response
        async Task OnResponse(object sender, SessionEventArgs e) {
            // read response headers
            var responseHeaders = e.HttpClient.Response.Headers;

            //if (!e.ProxySession.Request.Host.Equals("medeczane.sgk.gov.tr")) return;
            if (e.HttpClient.Request.Method == "GET" || e.HttpClient.Request.Method == "POST") {
                if (e.HttpClient.Response.StatusCode == 200) {
                    if (e.HttpClient.Response.ContentType != null && e.HttpClient.Response.ContentType.Trim().ToLower().Contains("text/html")) {
                        byte[] bodyBytes = await e.GetResponseBody();
                        e.SetResponseBody(bodyBytes);

                        string body = await e.GetResponseBodyAsString();
                        e.SetResponseBodyString(body);
                    }
                }
            }

            if (e.UserData != null) {
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
