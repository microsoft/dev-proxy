// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.MockResponses;
using Titanium.Web.Proxy.EventArguments;

namespace Microsoft.DevProxy.Plugins.RequestLogs;

public class MockGeneratorPlugin : BaseProxyPlugin
{
  public override string Name => nameof(MockGeneratorPlugin);

  public override void Register(IPluginEvents pluginEvents,
                          IProxyContext context,
                          ISet<UrlToWatch> urlsToWatch,
                          IConfigurationSection? configSection = null)
  {
    base.Register(pluginEvents, context, urlsToWatch, configSection);

    pluginEvents.AfterRecordingStop += AfterRecordingStop;
  }

  private Task AfterRecordingStop(object? sender, RecordingArgs e)
  {
    _logger?.LogInfo("Creating mocks from recorded requests...");

    if (!e.RequestLogs.Any())
    {
      _logger?.LogDebug("No requests to process");
      return Task.CompletedTask;
    }

    var methodAndUrlComparer = new MethodAndUrlComparer();
    var mocks = new List<MockResponse>();

    foreach (var request in e.RequestLogs)
    {
      if (request.MessageType != MessageType.InterceptedResponse ||
        request.Context is null ||
        request.Context.Session is null)
      {
        continue;
      }

      var methodAndUrlString = request.MessageLines.First();
      _logger?.LogDebug($"Processing request {methodAndUrlString}...");

      var methodAndUrl = GetMethodAndUrl(methodAndUrlString);
      var response = request.Context.Session.HttpClient.Response;

      var mock = new MockResponse
      {
        Method = methodAndUrl.Item1,
        Url = methodAndUrl.Item2,
        ResponseCode = response.StatusCode,
        ResponseHeaders = response.Headers
          .Select(h => new KeyValuePair<string, string>(h.Name, h.Value))
          .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
        ResponseBody = GetResponseBody(request.Context.Session).Result
      };
      // skip mock if it's 200 but has no body
      if (mock.ResponseCode == 200 && mock.ResponseBody is null)
      {
        _logger?.LogDebug("Skipping mock with 200 response code and no body");
        continue;
      }

      mocks.Add(mock);
      _logger?.LogDebug($"Added mock for {mock.Method} {mock.Url}");
    }

    _logger?.LogDebug($"Sorting mocks...");
    // sort mocks descending by url length so that most specific mocks are first
    mocks.Sort((a, b) => b.Url.CompareTo(a.Url));

    var mocksFile = new MockResponseConfiguration { Responses = mocks };

    _logger?.LogDebug($"Serializing mocks...");
    var mocksFileJson = JsonSerializer.Serialize(mocksFile, new JsonSerializerOptions { WriteIndented = true });
    var fileName = $"mocks-{DateTime.Now.ToString("yyyyMMddHHmmss")}.json";

    _logger?.LogDebug($"Writing mocks to {fileName}...");
    File.WriteAllText(fileName, mocksFileJson);

    _logger?.LogInfo($"Created mock file {fileName} with {mocks.Count} mocks");
    
    return Task.CompletedTask;
  }

  /// <summary>
  /// Returns the body of the response. For binary responses,
  /// saves the binary response as a file on disk and returns @filename
  /// </summary>
  /// <param name="session">Request session</param>
  /// <returns>Response body or @filename for binary responses</returns>
  private async Task<dynamic?> GetResponseBody(SessionEventArgs session)
  {
    _logger?.LogDebug("Getting response body...");

    var response = session.HttpClient.Response;
    if (response.ContentType is null || !response.HasBody)
    {
      _logger?.LogDebug("Response has no content-type set or has no body. Skipping");
      return null;
    }

    if (response.ContentType.Contains("application/json"))
    {
      _logger?.LogDebug("Response is JSON");

      try
      {
        _logger?.LogDebug("Reading response body as string...");
        var body = response.IsBodyRead ? response.BodyString : await session.GetResponseBodyAsString();
        _logger?.LogDebug($"Body: {body}");
        _logger?.LogDebug("Deserializing response body...");
        return JsonSerializer.Deserialize<dynamic>(body);
      }
      catch (Exception ex)
      {
        _logger?.LogError($"Error reading response body: {ex.Message}");
        return null;
      }
    }

    _logger?.LogDebug("Response is binary");
    // assume body is binary
    try
    {
      var filename = $"response-{DateTime.Now.ToString("yyyyMMddHHmmss")}.bin";
      _logger?.LogDebug("Reading response body as bytes...");
      var body = await session.GetResponseBody();
      _logger?.LogDebug($"Writing response body to {filename}...");
      File.WriteAllBytes(filename, body);
      return $"@{filename}";
    }
    catch (Exception ex)
    {
      _logger?.LogError($"Error reading response body: {ex.Message}");
      return null;
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
}
