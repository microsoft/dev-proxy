// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.using Microsoft.Extensions.Configuration;

using Microsoft.Graph.DeveloperProxy.Abstractions;

namespace Microsoft.Graph.DeveloperProxy;

internal class ProxyContext : IProxyContext
{
  public ILogger Logger { get; }
  public IProxyConfiguration Configuration { get; }

  public ProxyContext(ILogger logger, IProxyConfiguration configuration)
  {
    Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
  }
}
