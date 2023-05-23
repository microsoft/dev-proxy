// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Graph.DeveloperProxy.Plugins.RequestLogs;

internal enum PermissionsType
{
  [JsonPropertyName("application")]
  Application,
  [JsonPropertyName("delegated")]
  DelegatedWork
}

internal class MinimalPermissionsPluginConfiguration
{
  public PermissionsType Type { get; set; } = PermissionsType.DelegatedWork;
}

internal class RequestInfo
{
  [JsonPropertyName("requestUrl")]
  public string Url { get; set; }
  [JsonPropertyName("method")]
  public string Method { get; set; }
}

internal class PermissionInfo
{
  [JsonPropertyName("value")]
  public string Value { get; set; }
  [JsonPropertyName("scopeType")]
  public string ScopeType { get; set; }
  [JsonPropertyName("consentDisplayName")]
  public string ConsentDisplayName { get; set; }
  [JsonPropertyName("consentDescription")]
  public string ConsentDescription { get; set; }
  [JsonPropertyName("isAdmin")]
  public bool IsAdmin { get; set; }
  [JsonPropertyName("isLeastPrivilege")]
  public bool IsLeastPrivilege { get; set; }
  [JsonPropertyName("isHidden")]
  public bool IsHidden { get; set; }
}

internal class PermissionError
{
  [JsonPropertyName("requestUrl")]
  public string Url { get; set; }
  [JsonPropertyName("message")]
  public string Message { get; set; }
}

internal class ResultsAndErrors
{
  [JsonPropertyName("results")]
  public PermissionInfo[]? Results { get; set; }
  [JsonPropertyName("errors")]
  public PermissionError[]? Errors { get; set; }
}

internal class MethodAndUrlComparer : IEqualityComparer<Tuple<string, string>>
{
  public bool Equals(Tuple<string, string>? x, Tuple<string, string>? y)
  {
    if (object.ReferenceEquals(x, y))
    {
      return true;
    }

    if (object.ReferenceEquals(x, null) || object.ReferenceEquals(y, null))
    {
      return false;
    }

    return x.Item1 == y.Item1 && x.Item2 == y.Item2;
  }

  public int GetHashCode([DisallowNull] Tuple<string, string> obj)
  {
    if (obj == null)
    {
      return 0;
    }

    int methodHashCode = obj.Item1.GetHashCode();
    int urlHashCode = obj.Item2.GetHashCode();

    return methodHashCode ^ urlHashCode;
  }
}

public class MinimalPermissionsPlugin : BaseProxyPlugin
{
  public override string Name => nameof(MinimalPermissionsPlugin);
  private MinimalPermissionsPluginConfiguration _configuration = new();

  public override void Register(IPluginEvents pluginEvents,
                          IProxyContext context,
                          ISet<UrlToWatch> urlsToWatch,
                          IConfigurationSection? configSection = null)
  {
    base.Register(pluginEvents, context, urlsToWatch, configSection);

    configSection?.Bind(_configuration);

    pluginEvents.AfterRecordingStop += AfterRecordingStop;
  }
  private async void AfterRecordingStop(object? sender, RecordingArgs e)
  {
    if (!e.RequestLogs.Any())
    {
      return;
    }

    var methodAndUrlComparer = new MethodAndUrlComparer();
    var endpoints = new List<Tuple<string, string>>();

    foreach (var request in e.RequestLogs)
    {
      if (request.MessageType != MessageType.InterceptedRequest)
      {
        continue;
      }

      var methodAndUrlString = request.Message.First();
      var methodAndUrl = GetMethodAndUrl(methodAndUrlString);

      if (!ProxyUtils.IsGraphUrl(new Uri(methodAndUrl.Item2)))
      {
        continue;
      }

      methodAndUrl = new Tuple<string, string>(methodAndUrl.Item1, GetTokenizedUrl(methodAndUrl.Item2));

      endpoints.Add(methodAndUrl);
    }

    // Remove duplicates
    endpoints = endpoints.Distinct(methodAndUrlComparer).ToList();

    _logger?.LogInfo("Retrieving minimal permissions for:");
    _logger?.LogInfo(string.Join(Environment.NewLine, endpoints.Select(e => $"- {e.Item1} {e.Item2}")));
    _logger?.LogInfo("");

    _logger?.LogWarn("This plugin is in preview and may not return the correct results.");
    _logger?.LogWarn("Please review the permissions and test your app before using them in production.");
    _logger?.LogWarn("If you have any feedback, please open an issue at https://aka.ms/graph/proxy/issue.");
    _logger?.LogInfo("");

    await DetermineMinimalScopes(endpoints);
  }

  private async Task DetermineMinimalScopes(IEnumerable<Tuple<string, string>> endpoints)
  {
    var payload = endpoints.Select(e => new RequestInfo { Method = e.Item1, Url = e.Item2 });

    try
    {
      var url = $"https://graphexplorerapi-staging.azurewebsites.net/permissions?scopeType={_configuration.Type.ToString()}";
      using (var client = new HttpClient())
      {
        var stringPayload = JsonSerializer.Serialize(payload);
        _logger?.LogDebug($"Calling {url} with payload{Environment.NewLine}{stringPayload}");

        var response = await client.PostAsJsonAsync(url, payload);
        var content = await response.Content.ReadAsStringAsync();

        _logger?.LogDebug($"Response:{Environment.NewLine}{content}");

        var resultsAndErrors = JsonSerializer.Deserialize<ResultsAndErrors>(content);
        var minimalScopes = resultsAndErrors?.Results?.Select(p => p.Value).ToArray() ?? Array.Empty<string>();
        var errors = resultsAndErrors?.Errors?.Select(e => $"- {e.Url} ({e.Message})") ?? Array.Empty<string>();
        if (minimalScopes.Any())
        {
          _logger?.LogInfo("Minimal permissions:");
          _logger?.LogInfo(string.Join(", ", minimalScopes));
          _logger?.LogInfo("");
        }
        if (errors.Any())
        {
          _logger?.LogError("Couldn't determine minimal permissions for the following URLs:");
          _logger?.LogError(string.Join(Environment.NewLine, errors));
        }
      }
    }
    catch (Exception ex)
    {
      _logger?.LogError($"An error has occurred while retrieving minimal permissions: {ex.Message}");
    }
  }

  private Tuple<string, string> GetMethodAndUrl(string message)
  {
    var info = message.Split(" ");
    if (info.Length > 2)
    {
      info = new[] { info[0], String.Join(" ", info.Skip(1)) };
    }
    return new Tuple<string, string>(info[0], info[1]);
  }

  private string GetTokenizedUrl(string absoluteUrl)
  {
    var sanitizedUrl = ProxyUtils.SanitizeUrl(absoluteUrl);
    return "/" + String.Join("", new Uri(sanitizedUrl).Segments.Skip(2).Select(s => Uri.UnescapeDataString(s)));
  }
}
