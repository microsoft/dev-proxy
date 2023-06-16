// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.using Microsoft.Extensions.Configuration;

using Microsoft365.DeveloperProxy.Abstractions;

namespace Microsoft365.DeveloperProxy;

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
