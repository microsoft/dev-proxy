// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.Graph.DeveloperProxy.Plugins.RandomErrors;
internal enum GenericRandomErrorFailMode {
    Throttled,
    Random,
    PassThru
}

public class GenericRandomErrorConfiguration {
    public int Rate { get; set; } = 0;
    public string? ErrorsFile { get; set; }
    [JsonPropertyName("responses")]
    public IEnumerable<GenericErrorResponse> Responses { get; set; } = Array.Empty<GenericErrorResponse>();
}

public class GenericRandomErrorPlugin : BaseProxyPlugin {
    private readonly GenericRandomErrorConfiguration _configuration = new();
    private GenericErrorResponsesLoader? _loader = null;

    public override string Name => nameof(GenericRandomErrorPlugin);

    private const int retryAfterInSeconds = 5;
    private readonly Dictionary<string, DateTime> _throttledRequests;
    private readonly Random _random;

    public GenericRandomErrorPlugin() {
        _random = new Random();
        _throttledRequests = new Dictionary<string, DateTime>();
    }

    // uses config to determine if a request should be failed
    private GenericRandomErrorFailMode ShouldFail(ProxyRequestArgs e) {
        var r = e.Session.HttpClient.Request;
        string key = BuildThrottleKey(r);
        if (_throttledRequests.TryGetValue(key, out DateTime retryAfterDate)) {
            if (retryAfterDate > DateTime.Now) {
                _logger?.LogRequest(new[] { $"Calling {r.Url} again before waiting for the Retry-After period.", "Request will be throttled" }, MessageType.Failed, new LoggingContext(e.Session));
                // update the retryAfterDate to extend the throttling window to ensure that brute forcing won't succeed.
                _throttledRequests[key] = retryAfterDate.AddSeconds(retryAfterInSeconds);
                return GenericRandomErrorFailMode.Throttled;
            }
            else {
                // clean up expired throttled request and ensure that this request is passed through.
                _throttledRequests.Remove(key);
                return GenericRandomErrorFailMode.PassThru;
            }
        }
        return _random.Next(1, 100) <= _configuration.Rate ? GenericRandomErrorFailMode.Random : GenericRandomErrorFailMode.PassThru;
    }

    private void FailResponse(ProxyRequestArgs e, GenericRandomErrorFailMode failMode) {
        GenericErrorResponse error;
        if (failMode == GenericRandomErrorFailMode.Throttled) {
            error = new GenericErrorResponse {
                StatusCode = (int)HttpStatusCode.TooManyRequests,
                Headers = new Dictionary<string, string> {
                    { "Retry-After", retryAfterInSeconds.ToString() }
                }
            };
        }
        else {
            // pick a random error response for the current request
            error = _configuration.Responses.ElementAt(_random.Next(0, _configuration.Responses.Count()));
        }
        UpdateProxyResponse(e, error);
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
            _throttledRequests[BuildThrottleKey(request)] = retryAfterDate;
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
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), body.Trim('"').Substring(1));
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

    private string BuildThrottleKey(Request r) => $"{r.Method}-{r.Url}";

    public override void Register(IPluginEvents pluginEvents,
                         IProxyContext context,
                         ISet<Regex> urlsToWatch,
                         IConfigurationSection? configSection = null) {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        configSection?.Bind(_configuration);
        _loader = new GenericErrorResponsesLoader(_logger!, _configuration);

        pluginEvents.Init += OnInit;
        pluginEvents.BeforeRequest += OnRequest;
    }

    private void OnInit(object? sender, InitArgs e) {
        _loader?.InitResponsesWatcher();
    }

    private void OnRequest(object? sender, ProxyRequestArgs e) {
        var session = e.Session;
        var state = e.ResponseState;
        if (!e.ResponseState.HasBeenSet
            && _urlsToWatch is not null
            && e.ShouldExecute(_urlsToWatch)) {
            var failMode = ShouldFail(e);

            if (failMode == GenericRandomErrorFailMode.PassThru && _configuration.Rate != 100) {
                _logger?.LogRequest(new[] { "Passed through" }, MessageType.PassedThrough, new LoggingContext(e.Session));
                return;
            }
            FailResponse(e, failMode);
            state.HasBeenSet = true;
        }
    }
}
