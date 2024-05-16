// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Plugins.RequestLogs;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.Plugins.Reporters;

public class PlainTextReporter : BaseReporter
{
    public override string Name => nameof(PlainTextReporter);
    public override string FileExtension => ".txt";

    private readonly Dictionary<Type, Func<object, string?>> _transformers = new()
    {
        { typeof(ApiCenterOnboardingPluginReport), TransformApiCenterOnboardingReport },
        { typeof(ApiCenterProductionVersionPluginReport), TransformApiCenterProductionVersionReport },
        { typeof(ExecutionSummaryPluginReportByUrl), TransformExecutionSummaryByUrl },
        { typeof(ExecutionSummaryPluginReportByMessageType), TransformExecutionSummaryByMessageType },
        { typeof(MinimalPermissionsGuidancePluginReport), TransformMinimalPermissionsGuidanceReport },
        { typeof(MinimalPermissionsPluginReport), TransformMinimalPermissionsReport },
        { typeof(OpenApiSpecGeneratorPluginReport), TransformOpenApiSpecGeneratorReport }
    };

    private const string _requestsInterceptedMessage = "Requests intercepted";
    private const string _requestsPassedThroughMessage = "Requests passed through";

    protected override string? GetReport(KeyValuePair<string, object> report)
    {
        _logger?.LogDebug("Transforming {report}...", report.Key);

        var reportType = report.Value.GetType();

        if (_transformers.TryGetValue(reportType, out var transform))
        {
            _logger?.LogDebug("Transforming {reportType} using {transform}...", reportType.Name, transform.Method);

            return transform(report.Value);
        }
        else
        {
            _logger?.LogDebug("No transformer found for {reportType}", reportType.Name);
            return null;
        }
    }

    private static string? TransformOpenApiSpecGeneratorReport(object report)
    {
        var openApiSpecGeneratorReport = (OpenApiSpecGeneratorPluginReport)report;

        var sb = new StringBuilder();

        sb.AppendLine("Generated OpenAPI specs:");
        sb.AppendLine();
        sb.AppendJoin(Environment.NewLine, openApiSpecGeneratorReport.Select(i => $"- {i.FileName} ({i.ServerUrl})"));

        return sb.ToString();
    }

    private static string? TransformExecutionSummaryByMessageType(object report)
    {
        var executionSummaryReport = (ExecutionSummaryPluginReportByMessageType)report;

        var sb = new StringBuilder();

        sb.AppendLine("Dev Proxy execution summary");
        sb.AppendLine($"({DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")})");
        sb.AppendLine();

        sb.AppendLine(":: Message types".ToUpper());

        var data = executionSummaryReport.Data;
        var sortedMessageTypes = data.Keys.OrderBy(k => k);
        foreach (var messageType in sortedMessageTypes)
        {
            sb.AppendLine();
            sb.AppendLine(messageType.ToUpper());

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
                    sb.AppendLine(message);
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

        return sb.ToString();
    }

    private static string? TransformExecutionSummaryByUrl(object report)
    {
        var executionSummaryReport = (ExecutionSummaryPluginReportByUrl)report;

        var sb = new StringBuilder();

        sb.AppendLine("Dev Proxy execution summary");
        sb.AppendLine($"({DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")})");
        sb.AppendLine();

        sb.AppendLine(":: Requests".ToUpper());

        var data = executionSummaryReport.Data;
        var sortedMethodAndUrls = data.Keys.OrderBy(k => k);
        foreach (var methodAndUrl in sortedMethodAndUrls)
        {
            sb.AppendLine();
            sb.AppendLine(methodAndUrl);

            var sortedMessageTypes = data[methodAndUrl].Keys.OrderBy(k => k);
            foreach (var messageType in sortedMessageTypes)
            {
                sb.AppendLine();
                sb.AppendLine(messageType.ToUpper());
                sb.AppendLine();

                var sortedMessages = data[methodAndUrl][messageType].Keys.OrderBy(k => k);
                foreach (var message in sortedMessages)
                {
                    sb.AppendLine($"- ({data[methodAndUrl][messageType][message]}) {message}");
                }
            }
        }

        AddExecutionSummaryReportSummary(executionSummaryReport.Logs, sb);

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
        sb.AppendLine(":: Summary".ToUpper());
        sb.AppendLine();

        foreach (var messageType in data.Keys)
        {
            sb.AppendLine($"{messageType} ({data[messageType]})");
        }
    }

    private static string? TransformApiCenterProductionVersionReport(object report)
    {
        var getReadableApiStatus = (ApiCenterProductionVersionPluginReportItemStatus status) => status switch
        {
            ApiCenterProductionVersionPluginReportItemStatus.NotRegistered => "Not registered",
            ApiCenterProductionVersionPluginReportItemStatus.NonProduction => "Non-production",
            ApiCenterProductionVersionPluginReportItemStatus.Production => "Production",
            _ => "Unknown"
        };

        var apiCenterProductionVersionReport = (ApiCenterProductionVersionPluginReport)report;

        var groupedPerStatus = apiCenterProductionVersionReport
            .GroupBy(a => a.Status)
            .OrderBy(g => (int)g.Key);

        var sb = new StringBuilder();

        foreach (var group in groupedPerStatus)
        {
            sb.AppendLine($"{getReadableApiStatus(group.Key)} APIs:");
            sb.AppendLine();

            sb.AppendJoin(Environment.NewLine, group.Select(a => $"  {a.Method} {a.Url}"));
            sb.AppendLine();
        }

        return sb.ToString();
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

        if (apiCenterOnboardingReport.NewApis.Any())
        {
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

            sb.AppendLine();
        }

        if (apiCenterOnboardingReport.ExistingApis.Any())
        {
            sb.AppendLine("APIs that are already registered in Azure API Center:");
            sb.AppendLine();
            sb.AppendJoin(Environment.NewLine, apiCenterOnboardingReport.ExistingApis.Select(a => a.MethodAndUrl));
        }

        return sb.ToString();
    }

    private static string? TransformMinimalPermissionsReport(object report)
    {
        var minimalPermissionsReport = (MinimalPermissionsPluginReport)report;

        var sb = new StringBuilder();

        sb.AppendLine($"Minimal {minimalPermissionsReport.PermissionsType.ToString().ToLower()} permissions report");
        sb.AppendLine();
        sb.AppendLine("Requests:");
        sb.AppendLine();
        sb.AppendJoin(Environment.NewLine, minimalPermissionsReport.Requests.Select(r => $"- {r.Method} {r.Url}"));
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Minimal permissions:");
        sb.AppendLine();
        sb.AppendJoin(Environment.NewLine, minimalPermissionsReport.MinimalPermissions.Select(p => $"- {p}"));

        if (minimalPermissionsReport.Errors.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Couldn't determine minimal permissions for the following URLs:");
            sb.AppendLine();
            sb.AppendJoin(Environment.NewLine, minimalPermissionsReport.Errors.Select(e => $"- {e}"));
        }

        return sb.ToString();
    }

    private static string? TransformMinimalPermissionsGuidanceReport(object report)
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