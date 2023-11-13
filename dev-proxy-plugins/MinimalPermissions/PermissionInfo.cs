using System.Text.Json.Serialization;

namespace Microsoft.DevProxy.Plugins.RequestLogs.MinimalPermissions;

internal class PermissionInfo
{
  [JsonPropertyName("value")]
  public string Value { get; set; } = string.Empty;
  [JsonPropertyName("scopeType")]
  public string ScopeType { get; set; } = string.Empty;
  [JsonPropertyName("consentDisplayName")]
  public string ConsentDisplayName { get; set; } = string.Empty;
  [JsonPropertyName("consentDescription")]
  public string ConsentDescription { get; set; } = string.Empty;
  [JsonPropertyName("isAdmin")]
  public bool IsAdmin { get; set; }
  [JsonPropertyName("isLeastPrivilege")]
  public bool IsLeastPrivilege { get; set; }
  [JsonPropertyName("isHidden")]
  public bool IsHidden { get; set; }
}