// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Plugins.MinimalPermissions;

public class ApiPermissionError
{
    public required string Request { get; init; }
    public required string Error { get; init; }
}
