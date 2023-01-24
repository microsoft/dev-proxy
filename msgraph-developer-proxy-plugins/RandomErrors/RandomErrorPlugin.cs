// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.Graph.DeveloperProxy.Plugins.RandomErrors;
internal enum FailMode {
    Throttled,
    Random,
    PassThru
}

public class RandomErrorConfiguration {
    public int Rate { get; set; } = 0;
    public List<int> AllowedErrors { get; set; } = new();
}

public class RandomErrorPlugin : IProxyPlugin {
    private ISet<Regex>? _urlsToWatch;
    private ILogger? _logger;
    private readonly Option<int?> _rate;
    private readonly Option<IEnumerable<int>> _allowedErrors;
    private readonly RandomErrorConfiguration _configuration = new();

    public string Name => nameof(RandomErrorPlugin);

    private const int retryAfterInSeconds = 5;
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

    private readonly Dictionary<string, DateTime> _throttledRequests;
    private readonly Random _random;

    public RandomErrorPlugin() {
        _rate = new Option<int?>("--failure-rate", "The percentage of requests to graph to respond with failures");
        _rate.AddAlias("-f");
        _rate.ArgumentHelpName = "failure rate";
        _rate.AddValidator((input) => {
            int? value = input.GetValueForOption(_rate);
            if (value.HasValue && (value < 0 || value > 100)) {
                input.ErrorMessage = $"{value} is not a valid failure rate. Specify a number between 0 and 100";
            }
        });
        _allowedErrors = new Option<IEnumerable<int>>("--allowed-errors", "List of errors that the developer proxy may produce");
        _allowedErrors.AddAlias("-a");
        _allowedErrors.ArgumentHelpName = "allowed errors";
        _allowedErrors.AllowMultipleArgumentsPerToken = true;

        _random = new Random();
        _throttledRequests = new Dictionary<string, DateTime>();
    }

    // uses config to determine if a request should be failed
    private FailMode ShouldFail(ProxyRequestArgs e) {
        var r = e.Session.HttpClient.Request;
        string key = BuildThrottleKey(r);
        if (_throttledRequests.TryGetValue(key, out DateTime retryAfterDate)) {
            if (retryAfterDate > DateTime.Now) {
                _logger?.LogRequest(new[] { $"Calling {r.Url} again before waiting for the Retry-After period.", "Request will be throttled" }, MessageType.Failed, new LoggingContext(e.Session));
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
        return _random.Next(1, 100) <= _configuration.Rate ? FailMode.Random : FailMode.PassThru;
    }

    private void FailResponse(ProxyRequestArgs e, FailMode failMode) {
        HttpStatusCode errorStatus;
        if (failMode == FailMode.Throttled) {
            errorStatus = HttpStatusCode.TooManyRequests;
        }
        else {
            // pick a random error response for the current request method
            var methodStatusCodes = _methodStatusCode[e.Session.HttpClient.Request.Method];
            errorStatus = methodStatusCodes[_random.Next(0, methodStatusCodes.Length)];
        }
        UpdateProxyResponse(e, errorStatus);
    }

    private void UpdateProxyResponse(ProxyRequestArgs ev, HttpStatusCode errorStatus) {
        SessionEventArgs session = ev.Session;
        string requestId = Guid.NewGuid().ToString();
        string requestDate = DateTime.Now.ToString();
        Request request = session.HttpClient.Request;
        var headers = ProxyUtils.BuildGraphResponseHeaders(request, requestId, requestDate);
        if (errorStatus == HttpStatusCode.TooManyRequests) {
            var retryAfterDate = DateTime.Now.AddSeconds(retryAfterInSeconds);
            _throttledRequests[BuildThrottleKey(request)] = retryAfterDate;
            headers.Add(new HttpHeader("Retry-After", retryAfterInSeconds.ToString()));
        }

        string body = JsonSerializer.Serialize(new ErrorResponseBody(
            new ErrorResponseError {
                Code = new Regex("([A-Z])").Replace(errorStatus.ToString(), m => { return $" {m.Groups[1]}"; }).Trim(),
                Message = BuildApiErrorMessage(request),
                InnerError = new ErrorResponseInnerError {
                    RequestId = requestId,
                    Date = requestDate
                }
            })
        );
        _logger?.LogRequest(new[] { $"{(int)errorStatus} {errorStatus.ToString()}" }, MessageType.Chaos, new LoggingContext(ev.Session));
        session.GenericResponse(body ?? string.Empty, errorStatus, headers);
    }
    private static string BuildApiErrorMessage(Request r) => $"Some error was generated by the proxy. {(ProxyUtils.IsGraphRequest(r) ? ProxyUtils.IsSdkRequest(r) ? "" : MessageUtils.BuildUseSdkMessage(r) : "")}";

    private string BuildThrottleKey(Request r) => $"{r.Method}-{r.Url}";

    public void Register(IPluginEvents pluginEvents,
                         IProxyContext context,
                         ISet<Regex> urlsToWatch,
                         IConfigurationSection? configSection = null) {
        if (pluginEvents is null) {
            throw new ArgumentNullException(nameof(pluginEvents));
        }
        _urlsToWatch = urlsToWatch ?? throw new ArgumentNullException(nameof(urlsToWatch));
        _logger = context?.Logger ?? throw new ArgumentNullException(nameof(context));

        configSection?.Bind(_configuration);
        pluginEvents.Init += OnInit;
        pluginEvents.OptionsLoaded += OnOptionsLoaded;
        pluginEvents.BeforeRequest += OnRequest;
    }

    private void OnInit(object? sender, InitArgs e) {
        e.RootCommand.AddOption(_rate);
        e.RootCommand.AddOption(_allowedErrors);
    }

    private void OnOptionsLoaded(object? sender, OptionsLoadedArgs e) {
        InvocationContext context = e.Context;
        // configure probability of failure
        int? rate = context.ParseResult.GetValueForOption(_rate);
        if (rate.HasValue)
            _configuration.Rate = rate.Value;

        // Configure the allowed errors
        IEnumerable<int>? allowedErrors = context.ParseResult.GetValueForOption(_allowedErrors);
        if (allowedErrors?.Any() ?? false)
            _configuration.AllowedErrors = allowedErrors.ToList();

        if (_configuration.AllowedErrors.Any()) {
            foreach (string k in _methodStatusCode.Keys) {
                _methodStatusCode[k] = _methodStatusCode[k].Where(e => _configuration.AllowedErrors.Any(a => (int)e == a)).ToArray();
            }
        }
    }

    private void OnRequest(object? sender, ProxyRequestArgs e) {
        var session = e.Session;
        var state = e.ResponseState;
        if (!e.ResponseState.HasBeenSet
            && _urlsToWatch is not null
            && e.ShouldExecute(_urlsToWatch)) {
            var failMode = ShouldFail(e);

            if (failMode == FailMode.PassThru && _configuration.Rate != 100) {
                _logger?.LogRequest(new[] { "Passed through" }, MessageType.PassedThrough, new LoggingContext(e.Session));
                return;
            }
            FailResponse(e, failMode);
            state.HasBeenSet = true;
        }
    }
}


internal class ErrorResponseBody {
    [JsonPropertyName("error")]
    public ErrorResponseError Error { get; set; }

    public ErrorResponseBody(ErrorResponseError error) {
        Error = error;
    }
}

internal class ErrorResponseError {
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("innerError")]
    public ErrorResponseInnerError? InnerError { get; set; }
}

internal class ErrorResponseInnerError {
    [JsonPropertyName("request-id")]
    public string RequestId { get; set; } = string.Empty;
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;
}
