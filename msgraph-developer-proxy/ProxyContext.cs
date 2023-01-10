// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.using Microsoft.Extensions.Configuration;

using Microsoft.Graph.DeveloperProxy.Abstractions;

namespace Microsoft.Graph.DeveloperProxy;

internal class ProxyContext : IProxyContext {
    public ILogger Logger { get; }

    public ProxyContext(ILogger logger) {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
