// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Titanium.Web.Proxy;

namespace Microsoft.DevProxy;

public class ProxyState : IProxyState
{
    public bool IsRecording { get; set; } = false;
    public List<RequestLog> RequestLogs { get; } = [];
    public Dictionary<string, object> GlobalData { get; } = new() {
        { ProxyUtils.ReportsKey, new Dictionary<string, object>() }
    };
    public IProxyConfiguration ProxyConfiguration { get; }

    private readonly ILogger _logger;
    private readonly IPluginEvents _pluginEvents;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private ExceptionHandler _exceptionHandler => ex => _logger.LogError(ex, "An error occurred in a plugin");

    public ProxyState(ILogger logger, IPluginEvents pluginEvents, IHostApplicationLifetime hostApplicationLifetime, IProxyConfiguration proxyConfiguration)
    {
        _logger = logger;
        _pluginEvents = pluginEvents;
        _hostApplicationLifetime = hostApplicationLifetime;
        ProxyConfiguration = proxyConfiguration;
    }

    public void StartRecording()
    {
        if (IsRecording)
        {
            return;
        }

        IsRecording = true;
    }

    public async Task StopRecording()
    {
        if (!IsRecording)
        {
            return;
        }

        IsRecording = false;

        // clone the list so that we can clear the original
        // list in case a new recording is started, and
        // we let plugins handle previously recorded requests
        var clonedLogs = RequestLogs.ToArray();
        RequestLogs.Clear();
        await _pluginEvents.RaiseRecordingStopped(new RecordingArgs(clonedLogs)
        {
            GlobalData = GlobalData
        }, _exceptionHandler);
        _logger.LogInformation("DONE");
    }

    public void RaiseMockRequest()
    {
        _pluginEvents
            .RaiseMockRequest(new EventArgs(), _exceptionHandler)
            .GetAwaiter()
            .GetResult();
    }

    public void StopProxy()
    {
        _hostApplicationLifetime.StopApplication();
    }
}
