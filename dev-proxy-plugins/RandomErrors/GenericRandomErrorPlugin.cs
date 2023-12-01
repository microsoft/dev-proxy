// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.DevProxy.Plugins.RandomErrors;
internal enum GenericRandomErrorFailMode {
    Throttled,
    Random,
    PassThru
}

public class GenericRandomErrorConfiguration {
    public string? ErrorsFile { get; set; }
    [JsonPropertyName("responses")]
    public IEnumerable<GenericErrorResponse> Responses { get; set; } = Array.Empty<GenericErrorResponse>();
}

public class GenericRandomErrorPlugin : BaseProxyPlugin {
    private readonly GenericRandomErrorConfiguration _configuration = new();
    private GenericErrorResponsesLoader? _loader = null;
    private IProxyConfiguration? _proxyConfiguration;

    public override string Name => nameof(GenericRandomErrorPlugin);

    private const int retryAfterInSeconds = 5;
    private readonly Random _random;

    public GenericRandomErrorPlugin() {
        _random = new Random();
    }

    // uses config to determine if a request should be failed
    private GenericRandomErrorFailMode ShouldFail(ProxyRequestArgs e) => _random.Next(1, 100) <= _proxyConfiguration?.Rate ? GenericRandomErrorFailMode.Random : GenericRandomErrorFailMode.PassThru;

    private void FailResponse(ProxyRequestArgs e, GenericRandomErrorFailMode failMode) {
        // pick a random error response for the current request
        var error = _configuration.Responses.ElementAt(_random.Next(0, _configuration.Responses.Count()));
        UpdateProxyResponse(e, error);
    }

    private ThrottlingInfo ShouldThrottle(Request request, string throttlingKey) {
        var throttleKeyForRequest = BuildThrottleKey(request);
        return new ThrottlingInfo(throttleKeyForRequest == throttlingKey ? retryAfterInSeconds : 0, "Retry-After");
    }

    private void UpdateProxyResponse(ProxyRequestArgs ev, GenericErrorResponse error) {
        SessionEventArgs session = ev.Session;
        Request request = session.HttpClient.Request;
        var headers = error.Headers is not null ? 
            error.Headers.Select(h => new HttpHeader(h.Key, h.Value)).ToList() :
            new List<HttpHeader>();
        if (error.StatusCode == (int)HttpStatusCode.TooManyRequests &&
            error.AddDynamicRetryAfter.GetValueOrDefault(false)) {
            var retryAfterDate = DateTime.Now.AddSeconds(retryAfterInSeconds);
            ev.ThrottledRequests.Add(new ThrottlerInfo(BuildThrottleKey(request), ShouldThrottle, retryAfterDate));
            headers.Add(new HttpHeader("Retry-After", retryAfterInSeconds.ToString()));
        }

        var statusCode = (HttpStatusCode)error.StatusCode;
        var body = error.Body is null ? string.Empty : JsonSerializer.Serialize(error.Body);
        // we get a JSON string so need to start with the opening quote
        if (body.StartsWith("\"@")) {
            // we've got a mock body starting with @-token which means we're sending
            // a response from a file on disk
            // if we can read the file, we can immediately send the response and
            // skip the rest of the logic in this method
            // remove the surrounding quotes and the @-token
            var filePath = Path.Combine(Path.GetDirectoryName(_configuration.ErrorsFile) ?? "", ProxyUtils.ReplacePathTokens(body.Trim('"').Substring(1)));
            if (!File.Exists(filePath)) {
                _logger?.LogError($"File {filePath} not found. Serving file path in the mock response");
                session.GenericResponse(body, statusCode, headers);
            }
            else {
                var bodyBytes = File.ReadAllBytes(filePath);
                session.GenericResponse(bodyBytes, statusCode, headers);
            }
        }
        else {
            session.GenericResponse(body, statusCode, headers);
        }
        _logger?.LogRequest(new[] { $"{error.StatusCode} {statusCode.ToString()}" }, MessageType.Chaos, new LoggingContext(ev.Session));
    }

    // throttle requests per host
    private string BuildThrottleKey(Request r) => r.RequestUri.Host;

    public override void Register(IPluginEvents pluginEvents,
                         IProxyContext context,
                         ISet<UrlToWatch> urlsToWatch,
                         IConfigurationSection? configSection = null) {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        _proxyConfiguration = context.Configuration;

        configSection?.Bind(_configuration);
        _configuration.ErrorsFile = Path.GetFullPath(ProxyUtils.ReplacePathTokens(_configuration.ErrorsFile ?? string.Empty), Path.GetDirectoryName(_proxyConfiguration?.ConfigFile ?? string.Empty) ?? string.Empty);

        _loader = new GenericErrorResponsesLoader(_logger!, _configuration);

        pluginEvents.Init += OnInit;
        pluginEvents.BeforeRequest += OnRequest;
        
        // needed to get the failure rate configuration
        // must keep reference of the whole config rather than just rate
        // because rate is an int and can be set through command line args
        // which is done after plugins have been registered
        _proxyConfiguration = context.Configuration;
    }

    private void OnInit(object? sender, InitArgs e) {
        _loader?.InitResponsesWatcher();
    }

    private async Task OnRequest(object? sender, ProxyRequestArgs e) {
        var session = e.Session;
        var state = e.ResponseState;
        if (!e.ResponseState.HasBeenSet
            && _urlsToWatch is not null
            && e.ShouldExecute(_urlsToWatch)) {
            var failMode = ShouldFail(e);

            if (failMode == GenericRandomErrorFailMode.PassThru && _proxyConfiguration?.Rate != 100) {
                _logger?.LogRequest(new[] { "Passed through" }, MessageType.PassedThrough, new LoggingContext(e.Session));
                return;
            }
            FailResponse(e, failMode);
            state.HasBeenSet = true;
        }
    }
}
