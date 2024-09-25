// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

namespace Microsoft.DevProxy.Jwt;

internal static class JwtTokenGenerator
{
    internal static string CreateToken(JwtOptions jwtOptions)
    {
        var options = JwtCreatorOptions.Create(jwtOptions);

        var jwtIssuer = new JwtIssuer(
            options.Issuer,
            RandomNumberGenerator.GetBytes(32)
        );

        var jwtToken = jwtIssuer.CreateSecurityToken(options);

        var jwt = Jwt.Create(
            options.Scheme,
            jwtToken,
            new JwtSecurityTokenHandler().WriteToken(jwtToken),
            options.Scopes,
            options.Roles,
            options.Claims
        );

        return jwt.Token;
    }
}