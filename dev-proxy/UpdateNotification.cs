// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.DevProxy.Abstractions;

namespace Microsoft.DevProxy;

internal class ReleaseInfo {
    [JsonPropertyName("name")]
    public string? Version { get; set; }
    [JsonPropertyName("html_url")]
    public string? Url { get; set; }

    public ReleaseInfo() {
    }
}

internal static class UpdateNotification {
    private static readonly string releasesUrl = "https://aka.ms/devproxy/releases";

    /// <summary>
    /// Checks if a new version of the proxy is available.
    /// </summary>
    /// <returns>Instance of ReleaseInfo if a new version is available and null if the current version is the latest</returns>
    public static async Task<ReleaseInfo?> CheckForNewVersion() {
        try {
            var latestRelease = await GetLatestRelease();
            if (latestRelease == null || latestRelease.Version == null) {
                return null;
            }

            var latestReleaseVersion = new Version(latestRelease.Version.Substring(1)); // remove leading v
            var currentVersion = GetCurrentVersion();

            if (latestReleaseVersion > currentVersion) {
                return latestRelease;
            }
            else {
                return null;
            }
        }
        catch {
            return null;
        }
    }

    private static Version GetCurrentVersion() {
        var currentVersion = new Version(ProxyUtils.ProductVersion ?? "0.0.0.0");
        return currentVersion;
    }

    private static async Task<ReleaseInfo?> GetLatestRelease() {
        var http = new HttpClient();
        // GitHub API requires user agent to be set
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dev-proxy", "1.0"));
        var response = await http.GetStringAsync(releasesUrl);
        var releases = JsonSerializer.Deserialize<ReleaseInfo[]>(response);

        if (releases == null) {
            return null;
        }

        // we assume releases are sorted descending by their creation date
        foreach (var release in releases) {
            // skip preview releases
            if (release.Version == null ||
                release.Version.Contains("-")) {
                continue;
            }

            return release;
        }

        return null;
    }
}
