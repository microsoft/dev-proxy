// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http.Headers;
using Azure.Core;

namespace Microsoft.DevProxy.Plugins;

internal class AuthenticationDelegatingHandler : DelegatingHandler
{
    private readonly TokenCredential _credential;
    private readonly string[] _scopes;

    public AuthenticationDelegatingHandler(TokenCredential credential, string[] scopes)
    {
        _credential = credential;
        _scopes = scopes;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await _credential.GetTokenAsync(new TokenRequestContext(_scopes), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

        return await base.SendAsync(request, cancellationToken);
    }
}
