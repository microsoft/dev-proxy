// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.CommandHandlers;

public static class OutdatedCommandHandler
{
    public static async Task CheckVersion(bool versionOnly, ILogger logger)
    {
        var releaseInfo = await UpdateNotification.CheckForNewVersion(ProxyCommandHandler.Configuration.NewVersionNotification);

        if (releaseInfo is not null && releaseInfo.Version is not null)
        {
            var isBeta = releaseInfo.Version.Contains("-beta");

            if (versionOnly)
            {
                logger.LogInformation(releaseInfo.Version);
            }
            else
            {
                var notesLink = isBeta ? "https://aka.ms/devproxy/notes" : "https://aka.ms/devproxy/beta/notes";
                logger.LogInformation(
                    "New Dev Proxy version {version} is available.{newLine}Release notes: {link}{newLine}Docs: https://aka.ms/devproxy/upgrade",
                    releaseInfo.Version,
                    Environment.NewLine,
                    notesLink,
                    Environment.NewLine
                );
            }
        }
        else if (!versionOnly)
        {
            logger.LogInformation("You are using the latest version of Dev Proxy.");
        }
    }
}

