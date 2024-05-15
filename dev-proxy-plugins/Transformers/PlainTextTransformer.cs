using System.Text;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Transformers;

public class PlainTextTransformer : BaseProxyPlugin
{
    public override string Name => nameof(PlainTextTransformer);

    private readonly Dictionary<Type, Func<object, string>> _transformers = new()
    {
        { typeof(MinimalPermissionsPluginReport), TransformMinimalPermissionsReport },
        { typeof(MinimalPermissionsGuidancePluginReport), TransformMinimalPermissionsGuidanceReport },
        { typeof(ApiCenterOnboardingPluginReport), TransformApiCenterOnboardingReport },
        { typeof(ApiCenterProductionVersionPluginReport), TransformApiCenterProductionVersionReport }
    };

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        base.Register(pluginEvents, context, urlsToWatch, configSection);

        pluginEvents.AfterRecordingStop += AfterRecordingStop;
    }

    private Task AfterRecordingStop(object sender, RecordingArgs e)
    {
        if (!e.GlobalData.ContainsKey(ProxyUtils.ReportsKey) ||
            e.GlobalData[ProxyUtils.ReportsKey] is not Dictionary<string, object> reports ||
            !reports.Any())
        {
            _logger?.LogDebug("No reports found");
            return Task.CompletedTask;
        }

        foreach (var report in reports)
        {
            _logger?.LogDebug("Transforming {report}...", report.Key);

            var reportType = report.Value.GetType();

            if (_transformers.TryGetValue(reportType, out var transform))
            {
                _logger?.LogDebug("Transforming {reportType} using {transform}...", reportType.Name, transform.Method);

                reports[report.Key] = transform(report.Value);
            }
            else
            {
                _logger?.LogDebug("No transformer found for {reportType}", reportType.Name);
            }
        }

        return Task.CompletedTask;
    }

    private static string TransformApiCenterProductionVersionReport(object arg)
    {
        var apiCenterProductionVersionReport = (ApiCenterProductionVersionPluginReport)arg;

        var nonProductionApis = apiCenterProductionVersionReport
            .Where(a => a.Status == ApiCenterProductionVersionPluginReportItemStatus.NonProduction);

        if (!nonProductionApis.Any())
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        sb.AppendJoin(Environment.NewLine, nonProductionApis.Select(a => a.Recommendation));

        return sb.ToString();
    }

    private static string TransformApiCenterOnboardingReport(object report)
    {
        var apiCenterOnboardingReport = (ApiCenterOnboardingPluginReport)report;

        if (!apiCenterOnboardingReport.NewApis.Any())
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        var apisPerSchemeAndHost = apiCenterOnboardingReport.NewApis.GroupBy(x =>
        {
            var u = new Uri(x.Url);
            return u.GetLeftPart(UriPartial.Authority);
        });

        sb.AppendLine("New APIs that aren't registered in Azure API Center:");
        sb.AppendLine();

        foreach (var apiPerHost in apisPerSchemeAndHost)
        {
            sb.AppendLine($"{apiPerHost.Key}:");
            sb.AppendJoin(Environment.NewLine, apiPerHost.Select(a => $"  {a.Method} {a.Url}"));
        }

        return sb.ToString();
    }

    private static string TransformMinimalPermissionsReport(object report)
    {
        var minimalPermissionsReport = (MinimalPermissionsPluginReport)report;

        var sb = new StringBuilder();

        sb.AppendLine($"Minimal permissions:");
        sb.AppendLine();
        sb.AppendLine(string.Join(", ", minimalPermissionsReport.MinimalPermissions));
        
        if (minimalPermissionsReport.Errors.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Couldn't determine minimal permissions for the following URLs:");
            sb.AppendLine();
            sb.AppendLine(string.Join(Environment.NewLine, minimalPermissionsReport.Errors));
        }

        return sb.ToString();
    }

    private static string TransformMinimalPermissionsGuidanceReport(object report)
    {
        var minimalPermissionsGuidanceReport = (MinimalPermissionsGuidancePluginReport)report;

        var sb = new StringBuilder();

        var transformPermissionsInfo = (Action<MinimalPermissionsInfo, string>)((permissionsInfo, type) =>
        {
            sb.AppendLine($"{type} permissions for:");
            sb.AppendLine();
            sb.AppendLine(string.Join(Environment.NewLine, permissionsInfo.Operations.Select(o => $"- {o.Method} {o.Endpoint}")));
            sb.AppendLine();
            sb.AppendLine("Minimal permissions:");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", permissionsInfo.MinimalPermissions));
            sb.AppendLine();
            sb.AppendLine("Permissions on the token:");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", permissionsInfo.PermissionsFromTheToken));

            if (permissionsInfo.ExcessPermissions.Any())
            {
                sb.AppendLine();
                sb.AppendLine("The following permissions are unnecessary:");
                sb.AppendLine();
                sb.AppendLine(string.Join(", ", permissionsInfo.ExcessPermissions));
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("The token has the minimal permissions required.");
            }

            sb.AppendLine();
        });

        if (minimalPermissionsGuidanceReport.DelegatedPermissions is not null)
        {
            transformPermissionsInfo(minimalPermissionsGuidanceReport.DelegatedPermissions, "Delegated");
        }
        if (minimalPermissionsGuidanceReport.ApplicationPermissions is not null)
        {
            transformPermissionsInfo(minimalPermissionsGuidanceReport.ApplicationPermissions, "Application");
        }

        return sb.ToString();
    }
}