// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Titanium.Web.Proxy.Http;

namespace Microsoft.DevProxy.Plugins;

public class GraphUtils
{
    // throttle requests per workload
    public static string BuildThrottleKey(Request r) => BuildThrottleKey(r.RequestUri);

    public static string BuildThrottleKey(Uri uri)
    {
        if (uri.Segments.Length < 3)
        {
            return uri.Host;
        }

        // first segment is /
        // second segment is Graph version (v1.0, beta)
        // third segment is the workload (users, groups, etc.)
        // segment can end with / if there are other segments following
        var workload = uri.Segments[2].Trim('/');

        // TODO: handle 'me' which is a proxy to other resources

        return workload;
    }
}