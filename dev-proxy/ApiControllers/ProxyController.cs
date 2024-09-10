// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Mvc;

namespace Microsoft.DevProxy.ApiControllers;

[ApiController]
[Route("[controller]")]
public class ProxyController : ControllerBase
{
    private readonly IProxyState _proxyState;

    public ProxyController(IProxyState proxyState)
    {
        _proxyState = proxyState;
    }

    [HttpGet]
    public ProxyInfo Get() => ProxyInfo.From(_proxyState);

    [HttpPost]
    public async Task<IActionResult> SetAsync([FromBody] ProxyInfo proxyInfo)
    {
        if (proxyInfo.ConfigFile != null)
        {
            return BadRequest("ConfigFile cannot be set");
        }

        if (proxyInfo.Recording.HasValue)
        {
            if (proxyInfo.Recording.Value)
            {
                _proxyState.StartRecording();
            }
            else
            {
                await _proxyState.StopRecordingAsync();
            }
        }

        return Ok(ProxyInfo.From(_proxyState));
    }

    [HttpPost("raiseMockRequest")]
    public async Task RaiseMockRequestAsync()
    {
        await _proxyState.RaiseMockRequestAsync();
        Response.StatusCode = 202;
    }

    [HttpPost("stopProxy")]
    public void StopProxy()
    {
        Response.StatusCode = 202;
        _proxyState.StopProxy();
    }
}
