// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IdentityModel.Tokens.Jwt;

namespace Microsoft.DevProxy.Jwt;

internal record Jwt(string Id, string Scheme, string Name, string Audience, DateTimeOffset NotBefore, DateTimeOffset Expires, DateTimeOffset Issued, string Token)
{
    public IEnumerable<string> Scopes { get; set; } = [];

    public IEnumerable<string> Roles { get; set; } = [];

    public IDictionary<string, string> CustomClaims { get; set; } = new Dictionary<string, string>();

    public static Jwt Create(
        string scheme,
        JwtSecurityToken token,
        string encodedToken,
        IEnumerable<string> scopes,
        IEnumerable<string> roles,
        IDictionary<string, string> customClaims)
    {
        return new Jwt(token.Id, scheme, token.Subject, string.Join(", ", token.Audiences), token.ValidFrom, token.ValidTo, token.IssuedAt, encodedToken)
        {
            Scopes = scopes,
            Roles = roles,
            CustomClaims = customClaims
        };
    }
}