// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft.DevProxy.Plugins.Behavior;

public class RetryAfterPlugin : BaseProxyPlugin
{
    public override string Name => nameof(RetryAfterPlugin);

    public override void Register(IPluginEvents pluginEvents,
                         IProxyContext context,
                         ISet<UrlToWatch> urlsToWatch,
                         IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.BeforeRequest += OnRequest;
    }

    private Task OnRequest(object? sender, ProxyRequestArgs e)
    {
        if (e.ResponseState.HasBeenSet ||
            _urlsToWatch is null ||
            e.Session.HttpClient.Request.Method.ToUpper() == "OPTIONS" ||
            !e.ShouldExecute(_urlsToWatch))
        {
            return Task.CompletedTask;
        }

        ThrottleIfNecessary(e);
        return Task.CompletedTask;
    }

    private void ThrottleIfNecessary(ProxyRequestArgs e)
    {
        var request = e.Session.HttpClient.Request;
        var expiredThrottlers = e.ThrottledRequests.Where(t => t.ResetTime < DateTime.Now);
        foreach (var throttler in expiredThrottlers)
        {
            e.ThrottledRequests.Remove(throttler);
        }

        if (!e.ThrottledRequests.Any())
        {
            return;
        }

        foreach (var throttler in e.ThrottledRequests)
        {
            var throttleInfo = throttler.ShouldThrottle(request, throttler.ThrottlingKey);
            if (throttleInfo.ThrottleForSeconds > 0)
            {
                var messageLines = new[] { $"Calling {request.Url} before waiting for the Retry-After period.", "Request will be throttled.", $"Throttling on {throttler.ThrottlingKey}." };
                _logger?.LogRequest(messageLines, MessageType.Failed, new LoggingContext(e.Session));

                throttler.ResetTime = DateTime.Now.AddSeconds(throttleInfo.ThrottleForSeconds);
                UpdateProxyResponse(e, throttleInfo, string.Join(' ', messageLines));
                break;
            }
        }
    }

    private void UpdateProxyResponse(ProxyRequestArgs e, ThrottlingInfo throttlingInfo, string message)
    {
        var headers = new List<MockResponseHeader>();
        var body = string.Empty;
        var request = e.Session.HttpClient.Request;

        // override the response body and headers for the error response
        if (ProxyUtils.IsGraphRequest(request))
        {
            string requestId = Guid.NewGuid().ToString();
            string requestDate = DateTime.Now.ToString();
            headers.AddRange(ProxyUtils.BuildGraphResponseHeaders(request, requestId, requestDate));

            body = JsonSerializer.Serialize(new GraphErrorResponseBody(
                new GraphErrorResponseError
                {
                    Code = new Regex("([A-Z])").Replace(HttpStatusCode.TooManyRequests.ToString(), m => { return $" {m.Groups[1]}"; }).Trim(),
                    Message = BuildApiErrorMessage(request, message),
                    InnerError = new GraphErrorResponseInnerError
                    {
                        RequestId = requestId,
                        Date = requestDate
                    }
                })
            );
        }

        headers.Add(new(throttlingInfo.RetryAfterHeaderName, throttlingInfo.ThrottleForSeconds.ToString()));

        e.Session.GenericResponse(body ?? string.Empty, HttpStatusCode.TooManyRequests, headers.Select(h => new HttpHeader(h.Name, h.Value)));
        e.ResponseState.HasBeenSet = true;
    }

    private static string BuildApiErrorMessage(Request r, string message) => $"{message} {(ProxyUtils.IsGraphRequest(r) ? ProxyUtils.IsSdkRequest(r) ? "" : string.Join(' ', MessageUtils.BuildUseSdkForErrorsMessage(r)) : "")}";
}
