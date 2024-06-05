// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.Inspection.CDP;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Inspection;

public enum PreferredBrowser
{
    Edge,
    Chrome,
    EdgeDev
}

public class DevToolsPluginConfiguration
{
    public PreferredBrowser PreferredBrowser { get; set; } = PreferredBrowser.Edge;

    /// <summary>
    /// Path to the browser executable.
    /// If not set, the plugin will try to find the browser executable based on the PreferredBrowser.
    /// </summary>
    /// <remarks> It is recommended to set this value on Linux, where chrome can be installed with: npx playwright install chrome </remarks>
    public string PreferredBrowserPath { get; set; } = string.Empty;
}

public class DevToolsPlugin : BaseProxyPlugin
{
    private string socketUrl = string.Empty;
    private WebSocketServer? webSocket;
    private Dictionary<string, GetResponseBodyResultParams> responseBody = new();

    public override string Name => nameof(DevToolsPlugin);
    private readonly DevToolsPluginConfiguration _configuration = new();

    public DevToolsPlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    public override void Register()
    {
        base.Register();
        ConfigSection?.Bind(_configuration);

        InitInspector();

        PluginEvents.BeforeRequest += BeforeRequest;
        PluginEvents.AfterResponse += AfterResponse;
        PluginEvents.AfterRequestLog += AfterRequestLog;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private string GetChromiumProcessName(DevToolsPluginConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.PreferredBrowserPath))
        {
            Logger.LogInformation("PreferredBrowserPath was set to {path}. Ignoring PreferredBrowser setting.", configuration.PreferredBrowserPath);
            return configuration.PreferredBrowserPath;
        }

        // if not windows or OSX return error 
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Logger.LogError("Unsupported operating system. For Linux please set PreferredBrowserPath in the configuration.");
            return string.Empty;
        }

        return configuration.PreferredBrowser switch
        {
            PreferredBrowser.Chrome => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe")
                : "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            PreferredBrowser.EdgeDev => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge Dev\Application\msedge.exe")
                : "/Applications/Microsoft Edge Dev.app/Contents/MacOS/Microsoft Edge Dev",
            _ => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe")
                : "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
        };
    }

    private void InitInspector()
    {
        var port = GetFreePort();
        webSocket = new WebSocketServer(port, Logger);
        webSocket.MessageReceived += SocketMessageReceived;
        webSocket.Start();

        var processName = GetChromiumProcessName(_configuration);

        // check if the browser is installed
        if (!File.Exists(processName))
        {
            Logger.LogError("Browser executable not found at {processName}", processName);
            return;
        }

        // find if the process is already running
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));

        if (processes.Length > 0)
        {
            var ids = string.Join(", ", processes.Select(p => p.Id.ToString()));
            Logger.LogError(@"Found existing process {processName} with Id {processIds}. Could not start {name}. Please close existing browser windows before starting {name}", processName, ids, Name, Name);
            return;
        }

        var inspectionUrl = $"http://localhost:9222/devtools/inspector.html?ws=localhost:{port}";
        var args = $"{inspectionUrl} --remote-debugging-port=9222 --profile-directory=devproxy";

        Logger.LogInformation("{name} available at {inspectionUrl}", Name, inspectionUrl);

        var process = new Process
        {
            StartInfo = new()
            {
                FileName = processName,
                Arguments = args,
                // suppress default output
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };
        process.Start();
    }

    private void SocketMessageReceived(string msg)
    {
        if (webSocket is null)
        {
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize<GetResponseBodyMessage>(msg, ProxyUtils.JsonSerializerOptions);
            if (message?.Method == "Network.getResponseBody")
            {
                var requestId = message.Params?.RequestId;
                if (requestId is null ||
                    !responseBody.ContainsKey(requestId) ||
                    // should never happen because the message is sent from devtools
                    // and Id is required on all socket messages but theoretically
                    // it is possible
                    message.Id is null)
                {
                    return;
                }

                var result = new GetResponseBodyResult
                {
                    Id = (int)message.Id,
                    Result = new()
                    {
                        Body = responseBody[requestId].Body,
                        Base64Encoded = responseBody[requestId].Base64Encoded
                    }
                };
                _ = webSocket.SendAsync(result);
            }
        }
        catch { }
    }

    private string GetRequestId(Titanium.Web.Proxy.Http.Request? request)
    {
        if (request is null)
        {
            return string.Empty;
        }

        return request.GetHashCode().ToString();
    }

    private async Task BeforeRequest(object sender, ProxyRequestArgs e)
    {
        if (webSocket?.IsConnected != true)
        {
            return;
        }

        var requestId = GetRequestId(e.Session.HttpClient.Request);
        var headers = e.Session.HttpClient.Request.Headers
            .ToDictionary(h => h.Name, h => h.Value);

        var requestWillBeSentMessage = new RequestWillBeSentMessage
        {
            Params = new()
            {
                RequestId = requestId,
                LoaderId = "1",
                DocumentUrl = e.Session.HttpClient.Request.Url,
                Request = new()
                {
                    Url = e.Session.HttpClient.Request.Url,
                    Method = e.Session.HttpClient.Request.Method,
                    Headers = headers,
                    PostData = e.Session.HttpClient.Request.HasBody ? e.Session.HttpClient.Request.BodyString : null
                },
                Timestamp = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000,
                WallTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Initiator = new()
                {
                    Type = "other"
                }
            }
        };
        await webSocket.SendAsync(requestWillBeSentMessage);

        // must be included to avoid the "Provisional headers are shown" warning
        var requestWillBeSentExtraInfoMessage = new RequestWillBeSentExtraInfoMessage
        {
            Params = new()
            {
                RequestId = requestId,
                // must be included in the message or the message will be rejected
                AssociatedCookies = new object[0],
                Headers = headers
            }
        };
        await webSocket.SendAsync(requestWillBeSentExtraInfoMessage);
    }

    private async Task AfterResponse(object sender, ProxyResponseArgs e)
    {
        if (webSocket?.IsConnected != true)
        {
            return;
        }

        var body = new GetResponseBodyResultParams
        {
            Body = string.Empty,
            Base64Encoded = false
        };
        if (IsTextResponse(e.Session.HttpClient.Response.ContentType))
        {
            body.Body = e.Session.HttpClient.Response.BodyString;
            body.Base64Encoded = false;
        }
        else
        {
            body.Body = Convert.ToBase64String(e.Session.HttpClient.Response.Body);
            body.Base64Encoded = true;
        }
        responseBody.Add(e.Session.HttpClient.Request.GetHashCode().ToString(), body);

        var requestId = GetRequestId(e.Session.HttpClient.Request);

        var responseReceivedMessage = new ResponseReceivedMessage
        {
            Params = new()
            {
                RequestId = requestId,
                LoaderId = "1",
                Timestamp = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000,
                Type = "XHR",
                Response = new()
                {
                    Url = e.Session.HttpClient.Request.Url,
                    Status = e.Session.HttpClient.Response.StatusCode,
                    StatusText = e.Session.HttpClient.Response.StatusDescription,
                    Headers = e.Session.HttpClient.Response.Headers
                        .ToDictionary(h => h.Name, h => h.Value),
                    MimeType = e.Session.HttpClient.Response.ContentType
                }
            }
        };

        await webSocket.SendAsync(responseReceivedMessage);

        var loadingFinishedMessage = new LoadingFinishedMessage
        {
            Params = new()
            {
                RequestId = requestId,
                Timestamp = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000,
                EncodedDataLength = e.Session.HttpClient.Response.HasBody ? e.Session.HttpClient.Response.Body.Length : 0
            }
        };
        await webSocket.SendAsync(loadingFinishedMessage);
    }

    private bool IsTextResponse(string? contentType)
    {
        var isTextResponse = false;

        if (contentType is not null &&
            (contentType.IndexOf("text") > -1 ||
            contentType.IndexOf("json") > -1))
        {
            isTextResponse = true;
        }

        return isTextResponse;
    }

    private async void AfterRequestLog(object? sender, RequestLogArgs e)
    {
        if (webSocket?.IsConnected != true ||
            e.RequestLog.MessageType == MessageType.InterceptedRequest ||
            e.RequestLog.MessageType == MessageType.InterceptedResponse)
        {
            return;
        }

        var message = new EntryAddedMessage
        {
            Params = new()
            {
                Entry = new()
                {
                    Source = "network",
                    Text = string.Join(" ", e.RequestLog.MessageLines),
                    Level = Entry.GetLevel(e.RequestLog.MessageType),
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Url = e.RequestLog.Context?.Session.HttpClient.Request.Url,
                    NetworkRequestId = GetRequestId(e.RequestLog.Context?.Session.HttpClient.Request)
                }
            }
        };
        await webSocket.SendAsync(message);
    }
}
