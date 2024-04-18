using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy;

public static class OutdatedCommandHandler
{
    public static async Task CheckVersion(ILogger logger)
    {
        var releaseInfo = await UpdateNotification.CheckForNewVersion(ProxyCommandHandler.Configuration.NewVersionNotification);
        logger.LogInformation(releaseInfo?.Version);
    }
}

