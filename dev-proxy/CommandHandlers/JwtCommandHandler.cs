// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Microsoft.DevProxy.CommandHandlers;

public static class JwtCommandHandler
{
    public static void CreateToken(string scheme, string name, IEnumerable<string> audience, string issuer, IEnumerable<string> roles, IEnumerable<string> scopes, IEnumerable<string> claims, double validFor)
    {
        var jwtIssuer = new JwtIssuer(
            issuer,
            RandomNumberGenerator.GetBytes(32)
        );

        var options = new JwtCreatorOptions(
            scheme,
            name,
            audience,
            issuer,
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(validFor),
            roles,
            scopes,
            ParseClaims(claims)
        );

        var jwtToken = jwtIssuer.Create(options);

        var jwt = Jwt.Create(
            scheme,
            jwtToken,
            new JwtSecurityTokenHandler().WriteToken(jwtToken),
            options.Scopes,
            options.Roles,
            options.Claims
        );

        Console.WriteLine(jwt.Token);
    }

    private static Dictionary<string, string> ParseClaims(IEnumerable<string> claims) 
        => claims.Select(claim => claim.Split(" ")).ToDictionary(claimParts => claimParts[0], claimParts => claimParts[1]);
}

internal sealed class JwtIssuer(string issuer, byte[] signingKeyMaterial)
{
    private readonly SymmetricSecurityKey _signingKey = new(signingKeyMaterial);

    public string Issuer { get; } = issuer;

    public JwtSecurityToken Create(JwtCreatorOptions options)
    {
        var identity = new GenericIdentity(options.Name);

        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, options.Name));

        var id = Guid.NewGuid().ToString().GetHashCode().ToString("x", CultureInfo.InvariantCulture);
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Jti, id));

        if (options.Scopes is { } scopesToAdd)
        {
            identity.AddClaims(scopesToAdd.Select(s => new Claim("scope", s)));
        }

        if (options.Roles is { } rolesToAdd)
        {
            identity.AddClaims(rolesToAdd.Select(r => new Claim(ClaimTypes.Role, r)));
        }

        if (options.Claims is { Count: > 0 } claimsToAdd)
        {
            identity.AddClaims(claimsToAdd.Select(kvp => new Claim(kvp.Key, kvp.Value)));
        }

        // Although the JwtPayload supports having multiple audiences registered, the
        // creator methods and constructors don't provide a way of setting multiple
        // audiences. Instead, we have to register an `aud` claim for each audience
        // we want to add so that the multiple audiences are populated correctly.
        
        if (options.Audiences.ToList() is { Count: > 0 } audiences)
        {
            identity.AddClaims(audiences.Select(aud => new Claim(JwtRegisteredClaimNames.Aud, aud)));
        }

        var handler = new JwtSecurityTokenHandler();
        var jwtSigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature);
        var jwtToken = handler.CreateJwtSecurityToken(Issuer, audience: null, identity, options.NotBefore, options.ExpiresOn, issuedAt: DateTime.UtcNow, jwtSigningCredentials);
        return jwtToken;
    }
}

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

internal sealed record JwtCreatorOptions(
    string Scheme,
    string Name,
    IEnumerable<string> Audiences,
    string Issuer,
    DateTime NotBefore,
    DateTime ExpiresOn,
    IEnumerable<string> Roles,
    IEnumerable<string> Scopes,
    Dictionary<string, string> Claims);
