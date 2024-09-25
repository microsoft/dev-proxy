// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Jwt;

namespace Microsoft.DevProxy.CommandHandlers;

internal static class JwtCommandHandler
{
    internal static void GetToken(JwtOptions jwtOptions)
    {
        var token = JwtTokenGenerator.CreateToken(jwtOptions);

        Console.WriteLine(token);
    }
}
