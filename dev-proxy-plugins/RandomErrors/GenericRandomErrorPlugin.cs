// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using System.Net;
using System.Text.Json;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;
using Microsoft.DevProxy.Plugins.Behavior;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.RandomErrors;
internal enum GenericRandomErrorFailMode
{
    Throttled,
    Random,
    PassThru
}

public class GenericRandomErrorConfiguration
{
    public string? ErrorsFile { get; set; }
    public int RetryAfterInSeconds { get; set; } = 5;
    public IEnumerable<GenericErrorResponse> Responses { get; set; } = Array.Empty<GenericErrorResponse>();
}

public class GenericRandomErrorPlugin : BaseProxyPlugin
{
    private readonly GenericRandomErrorConfiguration _configuration = new();
    private GenericErrorResponsesLoader? _loader = null;
    private IProxyConfiguration? _proxyConfiguration;

    public override string Name => nameof(GenericRandomErrorPlugin);

    private readonly Random _random;

    public GenericRandomErrorPlugin()
    {
        _random = new Random();
    }

    // uses config to determine if a request should be failed
    private GenericRandomErrorFailMode ShouldFail(ProxyRequestArgs e) => _random.Next(1, 100) <= _proxyConfiguration?.Rate ? GenericRandomErrorFailMode.Random : GenericRandomErrorFailMode.PassThru;

    private void FailResponse(ProxyRequestArgs e, GenericRandomErrorFailMode failMode)
    {
        // pick a random error response for the current request
        var error = _configuration.Responses.ElementAt(_random.Next(0, _configuration.Responses.Count()));
        UpdateProxyResponse(e, error);
    }

    private ThrottlingInfo ShouldThrottle(Request request, string throttlingKey)
    {
        var throttleKeyForRequest = BuildThrottleKey(request);
        return new ThrottlingInfo(throttleKeyForRequest == throttlingKey ? _configuration.RetryAfterInSeconds : 0, "Retry-After");
    }

    private void UpdateProxyResponse(ProxyRequestArgs e, GenericErrorResponse error)
    {
        SessionEventArgs session = e.Session;
        Request request = session.HttpClient.Request;
        var headers = new List<MockResponseHeader>();
        if (error.Headers is not null)
        {
            headers.AddRange(error.Headers);
        }
        
        if (error.StatusCode == (int)HttpStatusCode.TooManyRequests &&
            error.Headers is not null &&
            error.Headers.FirstOrDefault(h => h.Name == "Retry-After" || h.Name == "retry-after")?.Value == "@dynamic")
        {
            var retryAfterDate = DateTime.Now.AddSeconds(_configuration.RetryAfterInSeconds);
            if (!e.GlobalData.ContainsKey(RetryAfterPlugin.ThrottledRequestsKey))
            {
                e.GlobalData.Add(RetryAfterPlugin.ThrottledRequestsKey, new List<ThrottlerInfo>());
            }
            var throttledRequests = e.GlobalData[RetryAfterPlugin.ThrottledRequestsKey] as List<ThrottlerInfo>;
            throttledRequests?.Add(new ThrottlerInfo(BuildThrottleKey(request), ShouldThrottle, retryAfterDate));
            // replace the header with the @dynamic value with the actual value
            var h = headers.First(h => h.Name == "Retry-After" || h.Name == "retry-after");
            headers.Remove(h);
            headers.Add(new("Retry-After", _configuration.RetryAfterInSeconds.ToString()));
        }

        var statusCode = (HttpStatusCode)error.StatusCode;
        var body = error.Body is null ? string.Empty : JsonSerializer.Serialize(error.Body, ProxyUtils.JsonSerializerOptions);
        // we get a JSON string so need to start with the opening quote
        if (body.StartsWith("\"@"))
        {
            // we've got a mock body starting with @-token which means we're sending
            // a response from a file on disk
            // if we can read the file, we can immediately send the response and
            // skip the rest of the logic in this method
            // remove the surrounding quotes and the @-token
            var filePath = Path.Combine(Path.GetDirectoryName(_configuration.ErrorsFile) ?? "", ProxyUtils.ReplacePathTokens(body.Trim('"').Substring(1)));
            if (!File.Exists(filePath))
            {
                _logger?.LogError("File {filePath} not found. Serving file path in the mock response", (string?)filePath);
                session.GenericResponse(body, statusCode, headers.Select(h => new HttpHeader(h.Name, h.Value)));
            }
            else
            {
                var bodyBytes = File.ReadAllBytes(filePath);
                session.GenericResponse(bodyBytes, statusCode, headers.Select(h => new HttpHeader(h.Name, h.Value)));
            }
        }
        else
        {
            session.GenericResponse(body, statusCode, headers.Select(h => new HttpHeader(h.Name, h.Value)));
        }
        _logger?.LogRequest(new[] { $"{error.StatusCode} {statusCode.ToString()}" }, MessageType.Chaos, new LoggingContext(e.Session));
    }

    // throttle requests per host
    private string BuildThrottleKey(Request r) => r.RequestUri.Host;

    public override void Register(IPluginEvents pluginEvents,
                         IProxyContext context,
                         ISet<UrlToWatch> urlsToWatch,
                         IConfigurationSection? configSection = null)
    {
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

    private void OnInit(object? sender, InitArgs e)
    {
        _loader?.InitResponsesWatcher();
    }

    private Task OnRequest(object? sender, ProxyRequestArgs e)
    {
        var state = e.ResponseState;
        if (!e.ResponseState.HasBeenSet
            && _urlsToWatch is not null
            && e.ShouldExecute(_urlsToWatch))
        {
            var failMode = ShouldFail(e);

            if (failMode == GenericRandomErrorFailMode.PassThru && _proxyConfiguration?.Rate != 100)
            {
                return Task.CompletedTask;
            }
            FailResponse(e, failMode);
            state.HasBeenSet = true;
        }

        return Task.CompletedTask;
    }
}
