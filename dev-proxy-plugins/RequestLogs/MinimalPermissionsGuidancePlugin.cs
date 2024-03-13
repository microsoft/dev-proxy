// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs.MinimalPermissions;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.DevProxy.Plugins.RequestLogs;

internal class MinimalPermissionsGuidancePluginConfiguration
{
    public string FilePath { get; set; } = "";
}

internal class OperationInfo
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;
}

internal class MinimalPermissionsInfo
{
    [JsonPropertyName("minimalPermissions")]
    public string[] MinimalPermissions { get; set; } = Array.Empty<string>();
    [JsonPropertyName("permissionsFromTheToken")]
    public string[] PermissionsFromTheToken { get; set; } = Array.Empty<string>();
    [JsonPropertyName("excessPermissions")]
    public string[] ExcessPermissions { get; set; } = Array.Empty<string>();
    [JsonPropertyName("operations")]
    public OperationInfo[] Operations { get; set; } = Array.Empty<OperationInfo>();
}

public class MinimalPermissionsGuidancePlugin : BaseProxyPlugin
{
    public override string Name => nameof(MinimalPermissionsGuidancePlugin);
    private MinimalPermissionsGuidancePluginConfiguration _configuration = new();
    private static readonly string _filePathOptionName = "--minimal-permissions-summary-file-path";

    public override Option[] GetOptions()
    {
        var filePath = new Option<string?>(_filePathOptionName, "Path to the file where the permissions summary should be saved. If not specified, the summary will be printed to the console. Path can be absolute or relative to the current working directory.")
        {
            ArgumentHelpName = "minimal-permissions-summary-file-path"
        };
        filePath.AddValidator(input =>
        {
            var outputFilePath = input.Tokens.First().Value;
            if (string.IsNullOrEmpty(outputFilePath))
            {
                return;
            }

            var dirName = Path.GetDirectoryName(outputFilePath);
            if (string.IsNullOrEmpty(dirName))
            {
                // current directory exists so no need to check
                return;
            }

            var outputDir = Path.GetFullPath(dirName);
            if (!Directory.Exists(outputDir))
            {
                input.ErrorMessage = $"The directory {outputDir} does not exist.";
            }
        });

        return [filePath];
    }

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        configSection?.Bind(_configuration);

        pluginEvents.OptionsLoaded += OptionsLoaded;
        pluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    private void OptionsLoaded(object? sender, OptionsLoadedArgs e)
    {
        InvocationContext context = e.Context;

        var filePath = context.ParseResult.GetValueForOption<string?>(_filePathOptionName, e.Options);
        if (filePath is not null)
        {
            _configuration.FilePath = filePath;
        }
    }

    private async Task AfterRecordingStop(object? sender, RecordingArgs e)
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

            var methodAndUrlString = request.MessageLines.First();
            var methodAndUrl = GetMethodAndUrl(methodAndUrlString);
            var requestsFromBatch = Array.Empty<Tuple<string, string>>();

            var uri = new Uri(methodAndUrl.Item2);
            if (!ProxyUtils.IsGraphUrl(uri))
            {
                continue;
            }

            if (ProxyUtils.IsGraphBatchUrl(uri))
            {
                var graphVersion = ProxyUtils.IsGraphBetaUrl(uri) ? "beta" : "v1.0";
                requestsFromBatch = GetRequestsFromBatch(request.Context?.Session.HttpClient.Request.BodyString!, graphVersion, uri.Host);
            }
            else
            {
                methodAndUrl = new Tuple<string, string>(methodAndUrl.Item1, GetTokenizedUrl(methodAndUrl.Item2));
            }

            var scopesAndType = GetPermissionsAndType(request);
            if (scopesAndType.Item1 == PermissionsType.Delegated)
            {
                // use the scopes from the last request in case the app is using incremental consent
                scopesToEvaluate = scopesAndType.Item2;

                if (ProxyUtils.IsGraphBatchUrl(uri))
                {
                    delegatedEndpoints.AddRange(requestsFromBatch);
                }
                else
                {
                    delegatedEndpoints.Add(methodAndUrl);
                }
            }
            else
            {
                // skip empty roles which are returned in case we couldn't get permissions information
                // 
                // application permissions are always the same because they come from app reg
                // so we can just use the first request that has them
                if (scopesAndType.Item2.Length > 0 &&
                  rolesToEvaluate.Length == 0)
                {
                    rolesToEvaluate = scopesAndType.Item2;

                    if (ProxyUtils.IsGraphBatchUrl(uri))
                    {
                        applicationEndpoints.AddRange(requestsFromBatch);
                    }
                    else
                    {
                        applicationEndpoints.Add(methodAndUrl);
                    }
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

        var minimalPermissionsInfo = new List<MinimalPermissionsInfo>();

        if (string.IsNullOrEmpty(_configuration.FilePath))
        {
            _logger?.LogWarning("This plugin is in preview and may not return the correct results.\r\nPlease review the permissions and test your app before using them in production.\r\nIf you have any feedback, please open an issue at https://aka.ms/devproxy/issue.\r\n");
        }

        if (delegatedEndpoints.Count > 0)
        {
            var delegatedPermissionsInfo = new MinimalPermissionsInfo();
            minimalPermissionsInfo.Add(delegatedPermissionsInfo);

            if (string.IsNullOrEmpty(_configuration.FilePath))
            {
                _logger?.LogInformation("Evaluating delegated permissions for:\r\n{endpoints}\r\n", string.Join(Environment.NewLine, delegatedEndpoints.Select(e => $"- {e.Item1} {e.Item2}")));
            }

            await EvaluateMinimalScopes(delegatedEndpoints, scopesToEvaluate, PermissionsType.Delegated, delegatedPermissionsInfo);
        }

        if (applicationEndpoints.Count > 0)
        {
            var applicationPermissionsInfo = new MinimalPermissionsInfo();
            minimalPermissionsInfo.Add(applicationPermissionsInfo);

            if (string.IsNullOrEmpty(_configuration.FilePath))
            {
                _logger?.LogInformation("Evaluating application permissions for:\r\n{applicationPermissions}\r\n", string.Join(Environment.NewLine, applicationEndpoints.Select(e => $"- {e.Item1} {e.Item2}")));
            }

            await EvaluateMinimalScopes(applicationEndpoints, rolesToEvaluate, PermissionsType.Application, applicationPermissionsInfo);
        }

        if (!string.IsNullOrEmpty(_configuration.FilePath))
        {
            var json = JsonSerializer.Serialize(minimalPermissionsInfo, ProxyUtils.JsonSerializerOptions);
            await File.WriteAllTextAsync(_configuration.FilePath, json);
        }
    }

    private Tuple<string, string>[] GetRequestsFromBatch(string batchBody, string graphVersion, string graphHostName)
    {
        var requests = new List<Tuple<string, string>>();

        if (String.IsNullOrEmpty(batchBody))
        {
            return requests.ToArray();
        }

        try
        {
            var batch = JsonSerializer.Deserialize<GraphBatchRequestPayload>(batchBody, ProxyUtils.JsonSerializerOptions);
            if (batch == null)
            {
                return requests.ToArray();
            }

            foreach (var request in batch.Requests)
            {
                try
                {
                    var method = request.Method;
                    var url = request.Url;
                    var absoluteUrl = $"https://{graphHostName}/{graphVersion}{url}";
                    requests.Add(new Tuple<string, string>(method, GetTokenizedUrl(absoluteUrl)));
                }
                catch { }
            }
        }
        catch { }

        return requests.ToArray();
    }

    /// <summary>
    /// Returns permissions and type (delegated or application) from the access token
    /// used on the request.
    /// If it can't get the permissions, returns PermissionType.Application for Item1
    /// and an empty array for Item2.
    /// </summary>
    private Tuple<PermissionsType, string[]> GetPermissionsAndType(RequestLog request)
    {
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

        try
        {
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
            else
            {
                return new Tuple<PermissionsType, string[]>(PermissionsType.Delegated, scopeClaim.Value.Split(' '));
            }
        }
        catch
        {
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

    private async Task EvaluateMinimalScopes(IEnumerable<Tuple<string, string>> endpoints, string[] permissionsFromAccessToken, PermissionsType scopeType, MinimalPermissionsInfo permissionsInfo)
    {
        var payload = endpoints.Select(e => new RequestInfo { Method = e.Item1, Url = e.Item2 });

        permissionsInfo.Operations = endpoints.Select(e => new OperationInfo
        {
            Method = e.Item1,
            Endpoint = e.Item2
        }).ToArray();
        permissionsInfo.PermissionsFromTheToken = permissionsFromAccessToken;

        try
        {
            var url = $"https://graphexplorerapi-staging.azurewebsites.net/permissions?scopeType={GetScopeTypeString(scopeType)}";
            using (var client = new HttpClient())
            {
                var stringPayload = JsonSerializer.Serialize(payload, ProxyUtils.JsonSerializerOptions);
                _logger?.LogDebug(string.Format("Calling {0} with payload{1}{2}", url, Environment.NewLine, stringPayload));

                var response = await client.PostAsJsonAsync(url, payload);
                var content = await response.Content.ReadAsStringAsync();

                _logger?.LogDebug(string.Format("Response:{0}{1}", Environment.NewLine, content));

                var resultsAndErrors = JsonSerializer.Deserialize<ResultsAndErrors>(content, ProxyUtils.JsonSerializerOptions);
                var minimalPermissions = resultsAndErrors?.Results?.Select(p => p.Value).ToArray() ?? Array.Empty<string>();
                var errors = resultsAndErrors?.Errors?.Select(e => $"- {e.Url} ({e.Message})") ?? Array.Empty<string>();
                if (minimalPermissions.Any())
                {
                    var excessPermissions = permissionsFromAccessToken
                      .Where(p => !minimalPermissions.Contains(p))
                      .ToArray();

                    permissionsInfo.MinimalPermissions = minimalPermissions;
                    permissionsInfo.ExcessPermissions = excessPermissions;

                    if (string.IsNullOrEmpty(_configuration.FilePath))
                    {
                        _logger?.LogInformation("Minimal permissions:\r\n{minimalPermissions}\r\nPermissions on the token:\r\n{tokenPermissions}", string.Join(", ", minimalPermissions), string.Join(", ", permissionsFromAccessToken));


                        if (excessPermissions.Any())
                        {
                            _logger?.LogWarning("The following permissions are unnecessary: {permissions}", excessPermissions);
                        }
                        else
                        {
                            _logger?.LogInformation("The token has the minimal permissions required.");
                        }
                    }
                }
                if (errors.Any())
                {
                    _logger?.LogError("Couldn't determine minimal permissions for the following URLs: {errors}", errors);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "An error has occurred while retrieving minimal permissions: {message}", ex.Message);
        }
    }

    private Tuple<string, string> GetMethodAndUrl(string message)
    {
        var info = message.Split(" ");
        if (info.Length > 2)
        {
            info = [info[0], string.Join(" ", info.Skip(1))];
        }
        return new Tuple<string, string>(info[0], info[1]);
    }

    private string GetTokenizedUrl(string absoluteUrl)
    {
        var sanitizedUrl = ProxyUtils.SanitizeUrl(absoluteUrl);
        return "/" + string.Join("", new Uri(sanitizedUrl).Segments.Skip(2).Select(Uri.UnescapeDataString));
    }
}
