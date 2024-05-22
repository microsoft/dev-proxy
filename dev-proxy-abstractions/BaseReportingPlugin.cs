// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Abstractions;

public abstract class BaseReportingPlugin : BaseProxyPlugin
{
    protected BaseReportingPlugin(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    protected virtual void StoreReport(object report, ProxyEventArgsBase e)
    {
        if (report is null)
        {
            return;
        }

        ((Dictionary<string, object>)e.GlobalData[ProxyUtils.ReportsKey])[Name] = report;
    }
}
