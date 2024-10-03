// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Plugins.MinimalPermissions;

public class ApiOperation
{
    public required string Method { get; init; }
    public required string OriginalUrl { get; init; }
    public required string TokenizedUrl { get; init; }
}