// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Microsoft.DevProxy.Jwt;

internal static class JwtTokenGenerator
{
    internal static string CreateToken(JwtOptions jwtOptions)
    {
        var options = JwtCreatorOptions.Create(jwtOptions);

        var jwtIssuer = new JwtIssuer(
            options.Issuer,
            Encoding.UTF8.GetBytes(options.SigningKey)
        );

        var jwtToken = jwtIssuer.CreateSecurityToken(options);
        return new JwtSecurityTokenHandler().WriteToken(jwtToken);
    }
}