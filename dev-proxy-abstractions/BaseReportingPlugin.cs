// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions;

public abstract class BaseReportingPlugin : BaseProxyPlugin
{
    protected virtual void StoreReport(object report, ProxyEventArgsBase e)
    {
        if (report is null)
        {
            return;
        }

        ((Dictionary<string, object>)e.GlobalData[ProxyUtils.ReportsKey])[Name] = report;
    }
}
