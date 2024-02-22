// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.using Microsoft.Extensions.Configuration;

using Microsoft.DevProxy.Abstractions;

namespace Microsoft.DevProxy;

internal class ProxyContext : IProxyContext
{
    public IProxyLogger Logger { get; }
    public IProxyConfiguration Configuration { get; }

    public ProxyContext(IProxyLogger logger, IProxyConfiguration configuration)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
}
