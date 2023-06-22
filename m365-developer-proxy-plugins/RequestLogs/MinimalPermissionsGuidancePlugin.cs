// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft365.DeveloperProxy.Abstractions;
using Microsoft365.DeveloperProxy.Plugins.RequestLogs.MinimalPermissions;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;

namespace Microsoft365.DeveloperProxy.Plugins.RequestLogs;

public class MinimalPermissionsGuidancePlugin : BaseProxyPlugin
{
  public override string Name => nameof(MinimalPermissionsGuidancePlugin);

  public override void Register(IPluginEvents pluginEvents,
                          IProxyContext context,
                          ISet<UrlToWatch> urlsToWatch,
                          IConfigurationSection? configSection = null)
  {
    base.Register(pluginEvents, context, urlsToWatch, configSection);

    pluginEvents.AfterRecordingStop += AfterRecordingStop;
  }
  private async void AfterRecordingStop(object? sender, RecordingArgs e)
  {
    if (!e.RequestLogs.Any())
    {
      return;
    }

    var methodAndUrlComparer = new MethodAndUrlComparer();
    var delegatedEndpoints = new List<Tuple<string, string>>();
    var applicationEndpoints = new List<Tuple<string, string>>();

    // scope for delegated permissions
    var scopesToEvaluate = Array.Empty<string>();
    // roles for application permissions
    var rolesToEvaluate = Array.Empty<string>();

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

      var scopesAndType = GetPermissionsAndType(request);
      if (scopesAndType.Item1 == PermissionsType.Delegated)
      {
        // use the scopes from the last request in case the app is using incremental consent
        scopesToEvaluate = scopesAndType.Item2;

        delegatedEndpoints.Add(methodAndUrl);
      }
      else
      {
        // skip empty roles which are returned in case we couldn't get permissions information
        // 
        // application permissions are always the same because they come from app reg
        // so we can just use the first request that has them
        if (scopesAndType.Item2.Length > 0 &&
          rolesToEvaluate.Length == 0) {
          rolesToEvaluate = scopesAndType.Item2;

          applicationEndpoints.Add(methodAndUrl);
        }
      }
    }

    // Remove duplicates
    delegatedEndpoints = delegatedEndpoints.Distinct(methodAndUrlComparer).ToList();
    applicationEndpoints = applicationEndpoints.Distinct(methodAndUrlComparer).ToList();

    if (delegatedEndpoints.Count == 0 && applicationEndpoints.Count == 0)
    {
      return;
    }

    _logger?.LogWarn("This plugin is in preview and may not return the correct results.");
    _logger?.LogWarn("Please review the permissions and test your app before using them in production.");
    _logger?.LogWarn("If you have any feedback, please open an issue at https://aka.ms/m365/proxy/issue.");
    _logger?.LogInfo("");

    if (delegatedEndpoints.Count > 0) {
      _logger?.LogInfo("Evaluating delegated permissions for:");
      _logger?.LogInfo("");
      _logger?.LogInfo(string.Join(Environment.NewLine, delegatedEndpoints.Select(e => $"- {e.Item1} {e.Item2}")));
      _logger?.LogInfo("");

      await EvaluateMinimalScopes(delegatedEndpoints, scopesToEvaluate, PermissionsType.Delegated);
    }

    if (applicationEndpoints.Count > 0) {
      _logger?.LogInfo("Evaluating application permissions for:");
      _logger?.LogInfo("");
      _logger?.LogInfo(string.Join(Environment.NewLine, applicationEndpoints.Select(e => $"- {e.Item1} {e.Item2}")));
      _logger?.LogInfo("");
      
      await EvaluateMinimalScopes(applicationEndpoints, rolesToEvaluate, PermissionsType.Application);
    }
  }

  /// <summary>
  /// Returns permissions and type (delegated or application) from the access token
  /// used on the request.
  /// If it can't get the permissions, returns PermissionType.Application for Item1
  /// and an empty array for Item2.
  /// </summary>
  private Tuple<PermissionsType, string[]> GetPermissionsAndType(RequestLog request) {
    var authHeader = request.Context?.Session.HttpClient.Request.Headers.GetFirstHeader("Authorization");
    if (authHeader == null)
    {
      return new Tuple<PermissionsType, string[]>(PermissionsType.Application, Array.Empty<string>());
    }

    var token = authHeader.Value.Replace("Bearer ", string.Empty);
    var tokenChunks = token.Split('.');
    if (tokenChunks.Length != 3)
    {
      return new Tuple<PermissionsType, string[]>(PermissionsType.Application, Array.Empty<string>());
    }

    try {
      var handler = new JwtSecurityTokenHandler();
      var jwtSecurityToken = handler.ReadJwtToken(token);

      var scopeClaim = jwtSecurityToken.Claims.FirstOrDefault(c => c.Type == "scp");
      if (scopeClaim == null)
      {
        // possibly an application token
        // roles is an array so we need to handle it differently
        var roles = jwtSecurityToken.Claims
          .Where(c => c.Type == "roles")
          .Select(c => c.Value)
          .ToArray();
        if (roles.Length == 0)
        {
          return new Tuple<PermissionsType, string[]>(PermissionsType.Application, Array.Empty<string>());
        }
        else
        {
          return new Tuple<PermissionsType, string[]>(PermissionsType.Application, roles);
        }
      }
      else {
        return new Tuple<PermissionsType, string[]>(PermissionsType.Delegated, scopeClaim.Value.Split(' '));
      }
    }
    catch {
      return new Tuple<PermissionsType, string[]>(PermissionsType.Application, Array.Empty<string>());
    }
  }

  private string GetScopeTypeString(PermissionsType scopeType)
  {
    return scopeType switch
    {
      PermissionsType.Application => "Application",
      PermissionsType.Delegated => "DelegatedWork",
      _ => throw new InvalidOperationException($"Unknown scope type: {scopeType}")
    };
  }

  private async Task EvaluateMinimalScopes(IEnumerable<Tuple<string, string>> endpoints, string[] permissionsFromAccessToken, PermissionsType scopeType)
  {
    var payload = endpoints.Select(e => new RequestInfo { Method = e.Item1, Url = e.Item2 });

    try
    {
      var url = $"https://graphexplorerapi-staging.azurewebsites.net/permissions?scopeType={GetScopeTypeString(scopeType)}";
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
          var excessPermissions = permissionsFromAccessToken
            .Where(p => !minimalScopes.Contains(p))
            .ToArray();

          _logger?.LogInfo("Minimal permissions:");
          _logger?.LogInfo(string.Join(", ", minimalScopes));
          _logger?.LogInfo("");
          _logger?.LogInfo("Permissions on the token:");
          _logger?.LogInfo(string.Join(", ", permissionsFromAccessToken));
          _logger?.LogInfo("");

          if (excessPermissions.Any())
          {
            _logger?.LogWarn("The following permissions are unnecessary:");
            _logger?.LogWarn(string.Join(", ", excessPermissions));
            _logger?.LogInfo("");
          }
          else
          {
            _logger?.LogInfo("The token has the minimal permissions required.");
            _logger?.LogInfo("");
          }
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
