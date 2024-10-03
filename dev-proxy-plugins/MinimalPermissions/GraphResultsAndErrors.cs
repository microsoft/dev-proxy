// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Plugins.MinimalPermissions;

internal class GraphResultsAndErrors
{
    public GraphPermissionInfo[]? Results { get; set; }
    public GraphPermissionError[]? Errors { get; set; }
}