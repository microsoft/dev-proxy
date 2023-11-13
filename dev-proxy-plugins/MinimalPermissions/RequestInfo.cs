
using System.Text.Json.Serialization;

namespace Microsoft.DevProxy.Plugins.RequestLogs.MinimalPermissions;

internal class RequestInfo
{
  [JsonPropertyName("requestUrl")]
  public string Url { get; set; } = string.Empty;
  [JsonPropertyName("method")]
  public string Method { get; set; } = string.Empty;
}