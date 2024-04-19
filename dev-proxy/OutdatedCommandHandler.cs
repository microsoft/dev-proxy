using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy;

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
                logger.LogInformation($"New Dev Proxy version {releaseInfo.Version} is available.{Environment.NewLine}Release notes: {notesLink}{Environment.NewLine}Docs: https://aka.ms/devproxy/upgrade");
            }
        }
        else if (!versionOnly)
        {
            logger.LogInformation("You are using the latest version of Dev Proxy.");
        }
    }
}

