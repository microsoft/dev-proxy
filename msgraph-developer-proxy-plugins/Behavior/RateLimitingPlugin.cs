// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.Graph.DeveloperProxy.Plugins.Behavior;

public class RateLimitConfiguration {
    public string HeaderLimit { get; set; } = "RateLimit-Limit";
    public string HeaderRemaining { get; set; } = "RateLimit-Remaining";
    public string HeaderReset { get; set; } = "RateLimit-Reset";
    public string HeaderRetryAfter { get; set; } = "Retry-After";
    public int CostPerRequest { get; set; } = 2;
    public int ResetTimeWindowSeconds { get; set; } = 60;
    public int WarningThresholdPercent { get; set; } = 80;
    public int RateLimit { get; set; } = 120;
    public int RetryAfterSeconds { get; set; } = 5;
}

public class RateLimitingPlugin : BaseProxyPlugin {
    public override string Name => nameof(RateLimitingPlugin);
    private readonly RateLimitConfiguration _configuration = new();
    private readonly Dictionary<string, DateTime> _throttledRequests = new();
    // initial values so that we know when we intercept the
    // first request and can set the initial values
    private int _resourcesRemaining = -1;
    private DateTime _resetTime = DateTime.MinValue;

    private bool ShouldForceThrottle(ProxyRequestArgs e) {
        var r = e.Session.HttpClient.Request;
        string key = BuildThrottleKey(r);
        if (_throttledRequests.TryGetValue(key, out DateTime retryAfterDate)) {
            if (retryAfterDate > DateTime.Now) {
                _logger?.LogRequest(new[] { $"Calling {r.Url} again before waiting for the Retry-After period.", "Request will be throttled" }, MessageType.Failed, new LoggingContext(e.Session));
                // update the retryAfterDate to extend the throttling window to ensure that brute forcing won't succeed.
                _throttledRequests[key] = retryAfterDate.AddSeconds(_configuration.RetryAfterSeconds);
                return true;
            }
            else {
                // clean up expired throttled request and ensure that this request is passed through.
                _throttledRequests.Remove(key);
                return false;
            }
        }

        return false;
    }

    private void ForceThrottleResponse(ProxyRequestArgs e) => UpdateProxyResponse(e, HttpStatusCode.TooManyRequests);

    private bool ShouldThrottle(ProxyRequestArgs e) {
        if (_resourcesRemaining > 0) {
            return false;
        }

        var r = e.Session.HttpClient.Request;
        string key = BuildThrottleKey(r);

        _logger?.LogRequest(new[] { $"Exceeded resource limit when calling {r.Url}.", "Request will be throttled" }, MessageType.Failed, new LoggingContext(e.Session));
        // update the retryAfterDate to extend the throttling window to ensure that brute forcing won't succeed.
        _throttledRequests[key] = DateTime.Now.AddSeconds(_configuration.RetryAfterSeconds);
        return true;
    }

    private void ThrottleResponse(ProxyRequestArgs e) => UpdateProxyResponse(e, HttpStatusCode.TooManyRequests);

    private void UpdateProxyResponse(ProxyHttpEventArgsBase e, HttpStatusCode errorStatus) {
        var headers = new List<HttpHeader>();
        var body = string.Empty;
        var request = e.Session.HttpClient.Request;

        // override the response body and headers for the error response
        if (errorStatus != HttpStatusCode.OK &&
            ProxyUtils.IsGraphRequest(request)) {
            string requestId = Guid.NewGuid().ToString();
            string requestDate = DateTime.Now.ToString();
            headers.AddRange(ProxyUtils.BuildGraphResponseHeaders(request, requestId, requestDate));

            body = JsonSerializer.Serialize(new GraphErrorResponseBody(
                new GraphErrorResponseError {
                    Code = new Regex("([A-Z])").Replace(errorStatus.ToString(), m => { return $" {m.Groups[1]}"; }).Trim(),
                    Message = BuildApiErrorMessage(request),
                    InnerError = new GraphErrorResponseInnerError {
                        RequestId = requestId,
                        Date = requestDate
                    }
                })
            );
        }

        // add rate limiting headers if reached the threshold percentage
        if (_resourcesRemaining <= _configuration.RateLimit - (_configuration.RateLimit * _configuration.WarningThresholdPercent / 100)) {
            headers.AddRange(new List<HttpHeader> {
                new HttpHeader(_configuration.HeaderLimit, _configuration.RateLimit.ToString()),
                new HttpHeader(_configuration.HeaderRemaining, _resourcesRemaining.ToString()),
                new HttpHeader(_configuration.HeaderReset, (_resetTime - DateTime.Now).TotalSeconds.ToString("N0")) // drop decimals
            });
        }

        // send an error response if we are (forced) throttling
        if (errorStatus == HttpStatusCode.TooManyRequests) {
            headers.Add(new HttpHeader(_configuration.HeaderRetryAfter, _configuration.RetryAfterSeconds.ToString()));

            e.Session.GenericResponse(body ?? string.Empty, errorStatus, headers);
            return;
        }

        if (errorStatus == HttpStatusCode.OK) {
            // add headers to the original API response
            e.Session.HttpClient.Response.Headers.AddHeaders(headers);
        }
    }
    private static string BuildApiErrorMessage(Request r) => $"Some error was generated by the proxy. {(ProxyUtils.IsGraphRequest(r) ? ProxyUtils.IsSdkRequest(r) ? "" : String.Join(' ', MessageUtils.BuildUseSdkForErrorsMessage(r)) : "")}";

    private string BuildThrottleKey(Request r) => $"{r.Method}-{r.Url}";

    public override void Register(IPluginEvents pluginEvents,
                         IProxyContext context,
                         ISet<Regex> urlsToWatch,
                         IConfigurationSection? configSection = null) {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        configSection?.Bind(_configuration);
        pluginEvents.BeforeRequest += OnRequest;
        pluginEvents.BeforeResponse += OnResponse;
    }

    // add rate limiting headers to the response from the API
    private void OnResponse(object? sender, ProxyResponseArgs e) {
        var session = e.Session;
        var state = e.ResponseState;
        if (_urlsToWatch is null ||
            !e.HasRequestUrlMatch(_urlsToWatch)) {
            return;
        }

        UpdateProxyResponse(e, HttpStatusCode.OK);
    }

    private void OnRequest(object? sender, ProxyRequestArgs e) {
        var session = e.Session;
        var state = e.ResponseState;
        if (e.ResponseState.HasBeenSet ||
            _urlsToWatch is null ||
            !e.ShouldExecute(_urlsToWatch)) {
            return;
        }

        // set the initial values for the first request
        if (_resetTime == DateTime.MinValue) {
            _resetTime = DateTime.Now.AddSeconds(_configuration.ResetTimeWindowSeconds);
        }
        if (_resourcesRemaining == -1) {
            _resourcesRemaining = _configuration.RateLimit;
        }

        // see if we passed the reset time window
        if (DateTime.Now > _resetTime) {
            _resourcesRemaining = _configuration.RateLimit;
            _resetTime = DateTime.Now.AddSeconds(_configuration.ResetTimeWindowSeconds);
        }

        // subtract the cost of the request
        _resourcesRemaining -= _configuration.CostPerRequest;
        // avoid communicating negative values
        if (_resourcesRemaining < 0) {
            _resourcesRemaining = 0;
        }

        if (ShouldForceThrottle(e)) {
            ForceThrottleResponse(e);
            state.HasBeenSet = true;
        }
        else if (ShouldThrottle(e)) {
            ThrottleResponse(e);
            state.HasBeenSet = true;
        }
    }
}


internal class GraphErrorResponseBody {
    [JsonPropertyName("error")]
    public GraphErrorResponseError Error { get; set; }

    public GraphErrorResponseBody(GraphErrorResponseError error) {
        Error = error;
    }
}

internal class GraphErrorResponseError {
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("innerError")]
    public GraphErrorResponseInnerError? InnerError { get; set; }
}

internal class GraphErrorResponseInnerError {
    [JsonPropertyName("request-id")]
    public string RequestId { get; set; } = string.Empty;
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;
}
