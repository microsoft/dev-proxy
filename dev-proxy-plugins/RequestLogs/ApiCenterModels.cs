// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;

namespace Microsoft.DevProxy.Plugins.RequestLogs.ApiCenter;

internal class Collection<T>()
{
  public T[] Value { get; set; } = [];
}

internal class Api
{
  public ApiProperties? Properties { get; set; }
  public string? Name { get; set; }
  public string? Id { get; set; }
}

internal class ApiProperties
{
  public string? Title { get; set; }
  public string? Summary { get; set; }
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public ApiKind? Kind { get; set; }
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public ApiLifecycleStage? LifecycleStage { get; set; }
  public ApiContact[] Contacts { get; set; } = [];
  public dynamic CustomProperties { get; set; } = new object();
}

internal class ApiContact
{
  public string? Name { get; set; }
  public string? Email { get; set; }
  public string? Url { get; set; }
}

internal class ApiDeployment
{
  public ApiDeploymentProperties? Properties { get; set; }
  public string? Name { get; set; }
}

internal class ApiDeploymentProperties
{
  public string? Title { get; set; }
  public string? DefinitionId { get; set; }
  public ApiDeploymentServer? Server { get; set; }
  public dynamic CustomProperties { get; set; } = new object();
}

internal class ApiDeploymentServer
{
  public string[] RuntimeUri { get; set; } = [];
}

internal class ApiDefinition
{
  public string? Id { get; set; }
  public ApiDefinitionProperties? Properties { get; set; }
  public OpenApiDocument? Definition { get; set; }
}

internal class ApiDefinitionProperties
{
  public ApiDefinitionPropertiesSpecification? Specification { get; set; }
}

internal class ApiDefinitionPropertiesSpecification
{
  public string? Name { get; set; }
}

internal class ApiSpecExportResult
{
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public ApiSpecExportResultFormat? Format { get; set; }
  public string? Value { get; set; }
}

internal class ApiVersion
{
  public ApiVersionProperties? Properties { get; set; }
  public string? Id { get; set; }
  public string? Name { get; set; }
}

internal class ApiVersionProperties
{
  public string? Title { get; set; }
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public ApiLifecycleStage LifecycleStage { get; set; }
}

internal enum ApiSpecExportResultFormat
{
  Inline,
  Link
}

internal enum ApiKind
{
  GraphQL,
  gRPC,
  REST,
  SOAP,
  Webhook,
  WebSocket
}

internal enum ApiLifecycleStage
{
  Deprecated,
  Design,
  Development,
  Preview,
  Production,
  Retired,
  Testing
}
