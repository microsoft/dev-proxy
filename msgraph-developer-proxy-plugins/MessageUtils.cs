// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy.Abstractions;
using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins;

internal class MessageUtils {
    public static string BuildUseSdkMessage(Request r) => $"To handle API errors more easily, use the Graph SDK. More info at {GetMoveToSdkUrl(r)}";

    public static string GetMoveToSdkUrl(Request request) {
        // TODO: return language-specific guidance links based on the language detected from the User-Agent
        return "https://aka.ms/move-to-graph-js-sdk";
    }
}
