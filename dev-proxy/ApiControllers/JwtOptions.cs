// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.ApiControllers;

public class JwtOptions
{
    public string? Name { get; set; }
    public IEnumerable<string>? Audiences { get; set; }
    public string? Issuer { get; set; }
    public IEnumerable<string>? Roles { get; set; }
    public IEnumerable<string>? Scopes { get; set; }
    public Dictionary<string, string>? Claims { get; set; }
    public double ValidFor { get; set; }
}

public class JwtInfo
{
    public required string Token { get; set; }
}
