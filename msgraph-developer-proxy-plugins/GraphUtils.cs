// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins;

public class GraphUtils
{
  // throttle requests per workload
  public static string BuildThrottleKey(Request r)
  {
    if (r.RequestUri.Segments.Length < 3)
    {
      return r.RequestUri.Host;
    }

    // first segment is /
    // second segment is Graph version (v1.0, beta)
    // third segment is the workload (users, groups, etc.)
    // segment can end with / if there are other segments following
    var workload = r.RequestUri.Segments[2].Trim('/');

    // TODO: handle 'me' which is a proxy to other resources

    return workload;
  }
}