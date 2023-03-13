// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Titanium.Web.Proxy.Http;

namespace Microsoft.Graph.DeveloperProxy.Plugins;

internal class MessageUtils {
    public static string[] BuildUseSdkForErrorsMessage(Request r) => new[] { "To handle API errors more easily, use the Microsoft Graph SDK.", $"More info at {GetMoveToSdkUrl(r)}" };

    public static string[] BuildUseSdkMessage(Request r) => new[] {
        "To more easily follow best practices for working with Microsoft Graph, ",
        "use the Microsoft Graph SDK.",
        $"More info at {GetMoveToSdkUrl(r)}"
    };

    public static string GetMoveToSdkUrl(Request request) {
        // TODO: return language-specific guidance links based on the language detected from the User-Agent
        return "https://aka.ms/graph/proxy/guidance/move-to-js-sdk";
    }
}
