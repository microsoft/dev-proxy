// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Plugins.MinimalPermissions;

public class ApiPermissionsInfo
{
    public required List<string> TokenPermissions { get; init; }
    public required List<ApiOperation> OperationsFromRequests { get; init; }
    public required string[] MinimalScopes { get; init; }
    public required string[] UnmatchedOperations { get; init; }
    public required List<ApiPermissionError> Errors { get; init; }
}