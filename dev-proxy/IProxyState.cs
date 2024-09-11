// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;

namespace Microsoft.DevProxy;

public interface IProxyState
{
    Dictionary<string, object> GlobalData { get; }
    bool IsRecording { get; }
    IProxyConfiguration ProxyConfiguration { get; }
    List<RequestLog> RequestLogs { get; }
    Task RaiseMockRequestAsync();
    void StartRecording();
    void StopProxy();
    Task StopRecordingAsync();
}