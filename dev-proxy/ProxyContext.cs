// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.using Microsoft.Extensions.Configuration;

using System.Security.Cryptography.X509Certificates;
using Microsoft.DevProxy.Abstractions;

namespace Microsoft.DevProxy;

internal class ProxyContext : IProxyContext
{
    public IProxyConfiguration Configuration { get; }
    public X509Certificate2? Certificate { get; }
    public ILanguageModelClient LanguageModelClient { get; }

    public ProxyContext(IProxyConfiguration configuration, X509Certificate2? certificate, ILanguageModelClient languageModelClient)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Certificate = certificate;
        LanguageModelClient = languageModelClient;
    }
}
