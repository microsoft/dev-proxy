// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http.Headers;
using Azure.Core;

namespace Microsoft.DevProxy.Plugins;

internal class AuthenticationDelegatingHandler : DelegatingHandler
{
    private readonly TokenCredential _credential;
    private readonly string[] _scopes;
    private DateTimeOffset? _expiresOn;
    private string? _accessToken;

    public AuthenticationDelegatingHandler(TokenCredential credential, string[] scopes)
    {
        _credential = credential;
        _scopes = scopes;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessToken(cancellationToken);
        
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await base.SendAsync(request, cancellationToken);
    }

    public async Task<string?> GetAccessToken(CancellationToken cancellationToken)
    {
        if (_expiresOn is null || _expiresOn < DateTimeOffset.UtcNow)
        {
            var accessToken = await _credential.GetTokenAsync(new TokenRequestContext(_scopes), cancellationToken);
            _expiresOn = accessToken.ExpiresOn;
            _accessToken = accessToken.Token;
        }

        return _accessToken;
    }
}
