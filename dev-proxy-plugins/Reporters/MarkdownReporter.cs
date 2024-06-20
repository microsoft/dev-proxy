// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Reporters;

public class MarkdownReporter : BaseReporter
{
    public override string Name => nameof(MarkdownReporter);
    public override string FileExtension => ".md";

    private readonly Dictionary<Type, Func<object, string?>> _transformers = new()
    {
        { typeof(ApiCenterMinimalPermissionsPluginReport), TransformApiCenterMinimalPermissionsReport },
        { typeof(ApiCenterOnboardingPluginReport), TransformApiCenterOnboardingReport },
        { typeof(ApiCenterProductionVersionPluginReport), TransformApiCenterProductionVersionReport },
        { typeof(ExecutionSummaryPluginReportByUrl), TransformExecutionSummaryByUrl },
        { typeof(ExecutionSummaryPluginReportByMessageType), TransformExecutionSummaryByMessageType },
        { typeof(HttpFileGeneratorPlugin), TransformHttpFileGeneratorReport },
        { typeof(MinimalPermissionsGuidancePluginReport), TransformMinimalPermissionsGuidanceReport },
        { typeof(MinimalPermissionsPluginReport), TransformMinimalPermissionsReport },
        { typeof(OpenApiSpecGeneratorPluginReport), TransformOpenApiSpecGeneratorReport }
    };

    private const string _requestsInterceptedMessage = "Requests intercepted";
    private const string _requestsPassedThroughMessage = "Requests passed through";

    public MarkdownReporter(IPluginEvents pluginEvents, IProxyContext context, ILogger logger, ISet<UrlToWatch> urlsToWatch, IConfigurationSection? configSection = null) : base(pluginEvents, context, logger, urlsToWatch, configSection)
    {
    }

    protected override string? GetReport(KeyValuePair<string, object> report)
    {
        Logger.LogDebug("Transforming {report}...", report.Key);

        var reportType = report.Value.GetType();

        if (_transformers.TryGetValue(reportType, out var transform))
        {
            Logger.LogDebug("Transforming {reportType} using {transform}...", reportType.Name, transform.Method.Name);

            return transform(report.Value);
        }
        else
        {
            Logger.LogDebug("No transformer found for {reportType}", reportType.Name);
            return null;
        }
    }

    private static string? TransformApiCenterOnboardingReport(object report)
    {
        var apiCenterOnboardingReport = (ApiCenterOnboardingPluginReport)report;

        if (!apiCenterOnboardingReport.NewApis.Any() &&
            !apiCenterOnboardingReport.ExistingApis.Any())
        {
            return null;
        }

        var sb = new StringBuilder();

        sb.AppendLine("# Azure API Center onboarding report");
        sb.AppendLine();

        if (apiCenterOnboardingReport.NewApis.Any())
        {
            var apisPerSchemeAndHost = apiCenterOnboardingReport.NewApis.GroupBy(x =>
            {
                var u = new Uri(x.Url);
                return u.GetLeftPart(UriPartial.Authority);
            });

            sb.AppendLine("## ⚠️ New APIs that aren't registered in Azure API Center");
            sb.AppendLine();

            foreach (var apiPerHost in apisPerSchemeAndHost)
            {
                sb.AppendLine($"### {apiPerHost.Key}");
                sb.AppendLine();
                sb.AppendJoin(Environment.NewLine, apiPerHost.Select(a => $"- {a.Method} {a.Url}"));
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        if (apiCenterOnboardingReport.ExistingApis.Any())
        {
            sb.AppendLine("## ✅ APIs that are already registered in Azure API Center");
            sb.AppendLine();
            sb.AppendLine("API|Definition ID|Operation ID");
            sb.AppendLine("---|------------|------------");
            sb.AppendJoin(Environment.NewLine, apiCenterOnboardingReport.ExistingApis.Select(a => $"{a.MethodAndUrl}|{a.ApiDefinitionId}|{a.OperationId}"));
            sb.AppendLine();
        }

        sb.AppendLine();

        return sb.ToString();
    }

    private static string? TransformApiCenterMinimalPermissionsReport(object report)
    {
        var apiCenterMinimalPermissionsReport = (ApiCenterMinimalPermissionsPluginReport)report;

        var sb = new StringBuilder();
        sb.AppendLine("# Azure API Center minimal permissions report")
            .AppendLine();

        sb.AppendLine("## ℹ️ Summary")
            .AppendLine()
            .AppendLine("<table>")
            .AppendFormat("<tr><td>🔎 APIs inspected</td><td align=\"right\">{0}</td></tr>{1}", apiCenterMinimalPermissionsReport.Results.Length, Environment.NewLine)
            .AppendFormat("<tr><td>🔎 Requests inspected</td><td align=\"right\">{0}</td></tr>{1}", apiCenterMinimalPermissionsReport.Results.Sum(r => r.Requests.Length), Environment.NewLine)
            .AppendFormat("<tr><td>✅ APIs called using minimal permissions</td><td align=\"right\">{0}</td></tr>{1}", apiCenterMinimalPermissionsReport.Results.Count(r => r.UsesMinimalPermissions), Environment.NewLine)
            .AppendFormat("<tr><td>🛑 APIs called using excessive permissions</td><td align=\"right\">{0}</td></tr>{1}", apiCenterMinimalPermissionsReport.Results.Count(r => !r.UsesMinimalPermissions), Environment.NewLine)
            .AppendFormat("<tr><td>⚠️ Unmatched requests</td><td align=\"right\">{0}</td></tr>{1}", apiCenterMinimalPermissionsReport.UnmatchedRequests.Length, Environment.NewLine)
            .AppendFormat("<tr><td>🛑 Errors</td><td align=\"right\">{0}</td></tr>{1}", apiCenterMinimalPermissionsReport.Errors.Length, Environment.NewLine)
            .AppendLine("</table>")
            .AppendLine();

        sb.AppendLine("## 🔌 APIs")
            .AppendLine();

        if (apiCenterMinimalPermissionsReport.Results.Any())
        {
            foreach (var apiResult in apiCenterMinimalPermissionsReport.Results)
            {
                sb.AppendFormat("### {0}{1}", apiResult.ApiName, Environment.NewLine)
                    .AppendLine()
                    .AppendFormat(apiResult.UsesMinimalPermissions ? "✅ Called using minimal permissions{0}" : "🛑 Called using excessive permissions{0}", Environment.NewLine)
                    .AppendLine()
                    .AppendLine("#### Permissions")
                    .AppendLine()
                    .AppendFormat("- Minimal permissions: {0}{1}", string.Join(", ", apiResult.MinimalPermissions.Order().Select(p => $"`{p}`")), Environment.NewLine)
                    .AppendFormat("- Permissions on the token: {0}{1}", string.Join(", ", apiResult.TokenPermissions.Order().Select(p => $"`{p}`")), Environment.NewLine)
                    .AppendFormat("- Excessive permissions: {0}{1}", apiResult.ExcessivePermissions.Any() ? string.Join(", ", apiResult.ExcessivePermissions.Order().Select(p => $"`{p}`")) : "none", Environment.NewLine)
                    .AppendLine()
                    .AppendLine("#### Requests")
                    .AppendLine()
                    .AppendJoin(Environment.NewLine, apiResult.Requests.Select(r => $"- {r}")).AppendLine()
                    .AppendLine();
            }
        }
        else
        {
            sb.AppendLine("No APIs found.")
                .AppendLine();
        }

        sb.AppendLine("## ⚠️ Unmatched requests")
            .AppendLine();

        if (apiCenterMinimalPermissionsReport.UnmatchedRequests.Any())
        {
            sb.AppendLine("The following requests were not matched to any API in API Center:")
                .AppendLine()
                .AppendJoin(Environment.NewLine, apiCenterMinimalPermissionsReport.UnmatchedRequests
                    .Select(r => $"- {r}").Order()).AppendLine()
                .AppendLine();
        }
        else
        {
            sb.AppendLine("No unmatched requests found.")
                .AppendLine();
        }

        sb.AppendLine("## 🛑 Errors")
            .AppendLine();

        if (apiCenterMinimalPermissionsReport.Errors.Any())
        {
            sb.AppendLine("The following errors occurred while determining minimal permissions:")
                .AppendLine()
                .AppendJoin(Environment.NewLine, apiCenterMinimalPermissionsReport.Errors
                    .OrderBy(o => o.Request)
                    .Select(e => $"- `{e.Request}`: {e.Error}")).AppendLine()
                .AppendLine();
        }
        else
        {
            sb.AppendLine("No errors occurred.");
        }

        return sb.ToString();
    }

    private static string? TransformApiCenterProductionVersionReport(object report)
    {
        var getReadableApiStatus = (ApiCenterProductionVersionPluginReportItemStatus status) => status switch
        {
            ApiCenterProductionVersionPluginReportItemStatus.NotRegistered => "🛑 Not registered",
            ApiCenterProductionVersionPluginReportItemStatus.NonProduction => "⚠️ Non-production",
            ApiCenterProductionVersionPluginReportItemStatus.Production => "✅ Production",
            _ => "Unknown"
        };

        var apiCenterProductionVersionReport = (ApiCenterProductionVersionPluginReport)report;

        var groupedPerStatus = apiCenterProductionVersionReport
            .GroupBy(a => a.Status)
            .OrderBy(g => (int)g.Key);

        var sb = new StringBuilder();
        sb.AppendLine("# Azure API Center lifecycle report");
        sb.AppendLine();

        foreach (var group in groupedPerStatus)
        {
            sb.AppendLine($"## {getReadableApiStatus(group.Key)} APIs");
            sb.AppendLine();

            if (group.Key == ApiCenterProductionVersionPluginReportItemStatus.NonProduction)
            {
                sb.AppendLine("API|Recommendation");
                sb.AppendLine("---|------------");
                sb.AppendJoin(Environment.NewLine, group
                    .OrderBy(a => a.Url)
                    .Select(a => $"{a.Method} {a.Url}|{a.Recommendation ?? ""}"));
                sb.AppendLine();
            }
            else
            {
                sb.AppendJoin(Environment.NewLine, group
                    .OrderBy(a => a.Url)
                    .Select(a => $"- {a.Method} {a.Url}"));
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string TransformExecutionSummaryByMessageType(object report)
    {
        var executionSummaryReport = (ExecutionSummaryPluginReportByMessageType)report;

        var sb = new StringBuilder();

        sb.AppendLine("# Dev Proxy execution summary");
        sb.AppendLine();
        sb.AppendLine($"Date: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
        sb.AppendLine();

        sb.AppendLine("## Message types");

        var data = executionSummaryReport.Data;
        var sortedMessageTypes = data.Keys.OrderBy(k => k);
        foreach (var messageType in sortedMessageTypes)
        {
            sb.AppendLine();
            sb.AppendLine($"### {messageType}");

            if (messageType == _requestsInterceptedMessage ||
                messageType == _requestsPassedThroughMessage)
            {
                sb.AppendLine();

                var sortedMethodAndUrls = data[messageType][messageType].Keys.OrderBy(k => k);
                foreach (var methodAndUrl in sortedMethodAndUrls)
                {
                    sb.AppendLine($"- ({data[messageType][messageType][methodAndUrl]}) {methodAndUrl}");
                }
            }
            else
            {
                var sortedMessages = data[messageType].Keys.OrderBy(k => k);
                foreach (var message in sortedMessages)
                {
                    sb.AppendLine();
                    sb.AppendLine($"#### {message}");
                    sb.AppendLine();

                    var sortedMethodAndUrls = data[messageType][message].Keys.OrderBy(k => k);
                    foreach (var methodAndUrl in sortedMethodAndUrls)
                    {
                        sb.AppendLine($"- ({data[messageType][message][methodAndUrl]}) {methodAndUrl}");
                    }
                }
            }
        }

        AddExecutionSummaryReportSummary(executionSummaryReport.Logs, sb);
        sb.AppendLine();

        return sb.ToString();
    }

    private static string TransformExecutionSummaryByUrl(object report)
    {
        var executionSummaryReport = (ExecutionSummaryPluginReportByUrl)report;

        var sb = new StringBuilder();

        sb.AppendLine("# Dev Proxy execution summary");
        sb.AppendLine();
        sb.AppendLine($"Date: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
        sb.AppendLine();

        sb.AppendLine("## Requests");

        var data = executionSummaryReport.Data;
        var sortedMethodAndUrls = data.Keys.OrderBy(k => k);
        foreach (var methodAndUrl in sortedMethodAndUrls)
        {
            sb.AppendLine();
            sb.AppendLine($"### {methodAndUrl}");

            var sortedMessageTypes = data[methodAndUrl].Keys.OrderBy(k => k);
            foreach (var messageType in sortedMessageTypes)
            {
                sb.AppendLine();
                sb.AppendLine($"#### {messageType}");
                sb.AppendLine();

                var sortedMessages = data[methodAndUrl][messageType].Keys.OrderBy(k => k);
                foreach (var message in sortedMessages)
                {
                    sb.AppendLine($"- ({data[methodAndUrl][messageType][message]}) {message}");
                }
            }
        }

        AddExecutionSummaryReportSummary(executionSummaryReport.Logs, sb);
        sb.AppendLine();

        return sb.ToString();
    }

    private static void AddExecutionSummaryReportSummary(IEnumerable<RequestLog> requestLogs, StringBuilder sb)
    {
        var getReadableMessageTypeForSummary = (MessageType messageType) => messageType switch
        {
            MessageType.Chaos => "Requests with chaos",
            MessageType.Failed => "Failures",
            MessageType.InterceptedRequest => _requestsInterceptedMessage,
            MessageType.Mocked => "Requests mocked",
            MessageType.PassedThrough => _requestsPassedThroughMessage,
            MessageType.Tip => "Tips",
            MessageType.Warning => "Warnings",
            _ => "Unknown"
        };

        var data = requestLogs
          .Where(log => log.MessageType != MessageType.InterceptedResponse)
          .Select(log => getReadableMessageTypeForSummary(log.MessageType))
          .OrderBy(log => log)
          .GroupBy(log => log)
          .ToDictionary(group => group.Key, group => group.Count());

        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("Category|Count");
        sb.AppendLine("--------|----:");

        foreach (var messageType in data.Keys)
        {
            sb.AppendLine($"{messageType}|{data[messageType]}");
        }
    }

    private static string? TransformMinimalPermissionsGuidanceReport(object report)
    {
        var minimalPermissionsGuidanceReport = (MinimalPermissionsGuidancePluginReport)report;

        var sb = new StringBuilder();
        sb.AppendLine("# Minimal permissions report");
        sb.AppendLine();

        var transformPermissionsInfo = (Action<MinimalPermissionsInfo, string>)((permissionsInfo, type) =>
        {
            sb.AppendLine($"## Minimal {type} permissions");
            sb.AppendLine();
            sb.AppendLine("### Operations");
            sb.AppendLine();
            sb.AppendJoin(Environment.NewLine, permissionsInfo.Operations.Select(o => $"- {o.Method} {o.Endpoint}"));
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### Minimal permissions");
            sb.AppendLine();
            sb.AppendJoin(Environment.NewLine, permissionsInfo.MinimalPermissions.Select(p => $"- {p}"));
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### Permissions on the token");
            sb.AppendLine();
            sb.AppendJoin(Environment.NewLine, permissionsInfo.PermissionsFromTheToken.Select(p => $"- {p}"));
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### Excessive permissions");

            if (permissionsInfo.ExcessPermissions.Any())
            {
                sb.AppendLine();
                sb.AppendLine("The following permissions included in token are unnecessary:");
                sb.AppendLine();
                sb.AppendJoin(Environment.NewLine, permissionsInfo.ExcessPermissions.Select(p => $"- {p}"));
                sb.AppendLine();
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
            transformPermissionsInfo(minimalPermissionsGuidanceReport.DelegatedPermissions, "delegated");
        }
        if (minimalPermissionsGuidanceReport.ApplicationPermissions is not null)
        {
            transformPermissionsInfo(minimalPermissionsGuidanceReport.ApplicationPermissions, "application");
        }

        return sb.ToString();
    }

    private static string? TransformMinimalPermissionsReport(object report)
    {
        var minimalPermissionsReport = (MinimalPermissionsPluginReport)report;

        var sb = new StringBuilder();
        sb.AppendLine($"# Minimal {minimalPermissionsReport.PermissionsType.ToString().ToLower()} permissions report");
        sb.AppendLine();

        sb.AppendLine("## Requests");
        sb.AppendLine();
        sb.AppendJoin(Environment.NewLine, minimalPermissionsReport.Requests.Select(r => $"- {r.Method} {r.Url}"));
        sb.AppendLine();

        sb.AppendLine();
        sb.AppendLine("## Minimal permissions");
        sb.AppendLine();
        sb.AppendJoin(Environment.NewLine, minimalPermissionsReport.MinimalPermissions.Select(p => $"- {p}"));
        sb.AppendLine();

        if (minimalPermissionsReport.Errors.Any())
        {
            sb.AppendLine();
            sb.AppendLine("## 🛑 Errors");
            sb.AppendLine();
            sb.AppendLine("Couldn't determine minimal permissions for the following URLs:");
            sb.AppendLine();
            sb.AppendJoin(Environment.NewLine, minimalPermissionsReport.Errors.Select(e => $"- {e}"));
            sb.AppendLine();
        }

        sb.AppendLine();

        return sb.ToString();
    }

    private static string? TransformOpenApiSpecGeneratorReport(object report)
    {
        var openApiSpecGeneratorReport = (OpenApiSpecGeneratorPluginReport)report;

        var sb = new StringBuilder();

        sb.AppendLine("# Generated OpenAPI specs");
        sb.AppendLine();
        sb.AppendLine("Server URL|File name");
        sb.AppendLine("---|---------");
        sb.AppendJoin(Environment.NewLine, openApiSpecGeneratorReport.Select(r => $"{r.ServerUrl}|{r.FileName}"));
        sb.AppendLine();
        sb.AppendLine();

        return sb.ToString();
    }

    private static string? TransformHttpFileGeneratorReport(object report)
    {
        var httpFileGeneratorReport = (HttpFileGeneratorPluginReport)report;

        var sb = new StringBuilder();

        sb.AppendLine("# Generated HTTP files");
        sb.AppendLine();
        sb.AppendJoin(Environment.NewLine, $"- {httpFileGeneratorReport}");
        sb.AppendLine();
        sb.AppendLine();

        return sb.ToString();
    }
}