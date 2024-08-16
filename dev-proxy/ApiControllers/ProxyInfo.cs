// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.ApiControllers;

public class ProxyInfo
{
    public bool? Recording { get; set; }
    public string? ConfigFile { get; init; }

    public static ProxyInfo From(IProxyState proxyState)
    {
        return new ProxyInfo
        {
            ConfigFile = proxyState.ProxyConfiguration.ConfigFile,
            Recording = proxyState.IsRecording
        };
    }
}
