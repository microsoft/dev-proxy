// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Plugins.MinimalPermissions;

internal class GraphPermissionInfo
{
    public string Value { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public string ConsentDisplayName { get; set; } = string.Empty;
    public string ConsentDescription { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsLeastPrivilege { get; set; }
    public bool IsHidden { get; set; }
}