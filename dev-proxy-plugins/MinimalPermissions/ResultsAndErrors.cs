namespace Microsoft.DevProxy.Plugins.RequestLogs.MinimalPermissions;

internal class ResultsAndErrors
{
    public PermissionInfo[]? Results { get; set; }
    public PermissionError[]? Errors { get; set; }
}