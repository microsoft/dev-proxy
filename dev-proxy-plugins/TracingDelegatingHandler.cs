// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins;

internal class TracingDelegatingHandler(ILogger logger) : DelegatingHandler
{
    private readonly ILogger _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(request.GetHashCode().ToString());

        _logger.LogTrace("Request: {method} {uri}", request.Method, request.RequestUri);
        foreach (var (header, value) in request.Headers)
        {
            _logger.LogTrace("{header}: {value}", header, string.Join(", ", value));
        }
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync();
            _logger.LogTrace("Body: {body}", body);
        }

        var response = await base.SendAsync(request, cancellationToken);

        _logger.LogTrace("Response");
        foreach (var (header, value) in response.Headers)
        {
            _logger.LogTrace("{header}: {value}", header, string.Join(", ", value));
        }
        if (response.Content is not null)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogTrace("Body: {body}", body);
        }

        return response;
    }
}