// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy;

static class Announcement
{
    private static readonly string announcementUrl = "https://aka.ms/devproxy/announcement";

    public static async Task ShowAsync()
    {
        var announcement = await GetAsync();
        if (!string.IsNullOrEmpty(announcement))
        {
            await Console.Error.WriteLineAsync(announcement);
        }
    }

    public static async Task<string?> GetAsync()
    {
        try
        {
            using var client = new HttpClient();
            return await client.GetStringAsync(announcementUrl);
        }
        catch
        {
            return null;
        }
    }
}