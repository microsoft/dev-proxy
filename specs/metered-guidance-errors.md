# Metered APIs guidance and random errors

Using the Metered Microsoft Graph APIs plugin, customers can understand if they use metered APIs in Microsoft Graph and can test how their application will response to 402 Payment Required responses.

Metered APIs allows you to get access to advanced capabilities in Microsoft Graph. They require an active Azure subscription with the calling application. Some of these metered APIs are protected and require additional validation beyond permissions and admin consent. The main goal of this guidance is to know that the APIs they are calling would require extra action when deployed in production and could incur a cost.

## History

| Version | Date       | Comments               | Author           |
| ------- | ---------- | ---------------------- | ---------------- |
| 1.0     | 2023-03-20 | Initial specifications | @sebastienlevert |

## Configuration

N/A

## Implementation

> **Note**
> This metered APIs plugin is only valid for Microsoft Graph requests.

### Guidance

Metered APIs guidance will be implemented as a plugin, subscribed to one events: `BeforeRequest`.

In the `BeforeRequest` event, the plugin will check if the request URL is part of the identified metered APIs. This list of APIs is documented in the official Microsoft Graph Docs [here](https://learn.microsoft.com/en-us/graph/metered-api-list?view=graph-rest-1.0). This list will likely grow but as a first step, we will be using a static configuration file to identify the endpoints to mark as metered. If the request is targeted at a metered API, we will emit a log message that will include text telling the developer the endpoint is metered and will include a link to the Metered APIs and services in Microsoft Graph documentation: `https://learn.microsoft.com/en-us/graph/metered-api-list`.

#### Passthrough

- The 'More info' is coming from the `billingInformation` property.
- The 'Request access' is coming from the `registrationForm` property.

```shell
[ REQUEST ]    GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages?model=A
[   API   ]  ┌ Passed through
             └ GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages?model=A
[   INFO  ]  ┌ This is a metered API.
             │ More info at https://learn.microsoft.com/en-us/graph/teams-licenses
             │ Request access at https://aka.ms/teamsgraph/requestaccess
             └ GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages?model=A
```

#### Using the API without a payment model

- The 'More info' is coming from the `billingInformation` property.

```shell
[ REQUEST ]    GET hhttps://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages
[   API   ]  ┌ Passed through
             └ GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages
[ WARNING ]  ┌ You are using a metered API without a payment model parameter. You are currently in Evaluation Mode and the API will eventually stop returning data
             │ More info at https://learn.microsoft.com/en-us/graph/teams-licenses#evaluation-mode-default-requirements
             └ GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages
```

#### Using the API with an invalid payment model

- The 'More info' is coming from the `billingInformation` property.

```shell
[ REQUEST ]    GET hhttps://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages?model=randomModel
[   API   ]  ┌ Passed through
             └ GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages?model=randomModel
[ WARNING ]  ┌ You are using a metered API with an invalid a payment model parameter (`randomModel`).
             │ More info at https://learn.microsoft.com/en-us/graph/teams-licenses#payment-models
             └ GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages
```

### Random Errors

- When registered, the plugin randomly generates 3 new HTTP 402 errors.
- The `--failure-rate` should be the same that the one provided on the `GraphRandomErrorPlugin`.
- 402s are dependent on the HTTP Verb that is used. In the configuration file, HTTP Verbs need to be explicit.
- 402s should only be returned for the metered endpoints and only for the HTTP Verb used.
- The list of 402s is available [here](https://learn.microsoft.com/en-us/graph/teams-licenses#payment-related-errors):

| Error code             | Scenario                                         | HTTP Verb    | Sample error message                                                                                      |
| :--------------------- | :----------------------------------------------- | :----------- | :-------------------------------------------------------------------------------------------------------- |
| 402 (Payment Required) | Passing `model=A` without a Microsoft E5 license | GET          | `...needs a valid license to access this API...`, `...tenant needs a valid license to access this API...` |
| 402 (Payment Required) | Calling Patch API passing `model=B`              | PATCH        | `...query parameter 'model' does not support value 'B' for this API. Use billing model 'A'...`            |
| 402 (Payment Required) | `Evaluation mode` capacity exceeded              | GET or PATCH | `...evaluation mode capacity has been exceeded. Use a valid billing model...`                             |

> **Note**
> Full error messages are currently not available and we don't have specific information on the ODSP HTTP Errors yet. When we will get them, we will update the plugin to include them.

#### Payment Required

```shell
[ REQUEST ]    GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages?model=A
[  CHAOS  ]  ┌ 402 PaymentRequired (Needs a valid license to access this API)
             └ GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages?model=A
```

## Configuration file

The plugin will have its own preset file. This configuration file is where metered APIs will be listed. This file should have default values for all supported Microsoft Graph clouds. As of today, only the commercial cloud supports Metered APIs.

- Each `urlToWatch` are rolled up to a single metered API. Multiple `urlToWatch` value means each endpoint behaves the same and should be treated as a metered API.
- `supportedPaymentModels` are query string passed to the endpoint. If not present, they are considered in Evaluation Mode. If present, they need to be available in the `supportedPaymentModels` list.
- `registrationForm` represents an URL to request access to the API (if any).
- `httpVerbs` represents an array of HTTP Verbs that are effectively metered. If none are provided, we assume GET.
- `registrationForm` represents an URL to request access to the API (if any).

```json
{
  "plugins": [
    {
      "name": "GraphMeteredGuidancePlugin",
      "enabled": true,
      "pluginPath": "GraphProxyPlugins\\msgraph-developer-proxy-plugins.dll",
      "configSection": "graphMeteredGuidancePlugin"
    }
  ],
  "graphMeteredGuidancePlugin": [
    {
      "urlsToWatch": [
        "https://graph.microsoft.com/*/users/*/chats/getAllMessages"
      ],
      "supportedPaymentModels": ["model=A", "model=B"],
      "billingInformation": "https://learn.microsoft.com/en-us/graph/teams-licenses",
      "registrationForm": "https://aka.ms/teamsgraph/requestaccess"
    },
    {
      "urlsToWatch": [
        "https://graph.microsoft.com/*/teams/*/channels/getAllMessages"
      ],
      "supportedPaymentModels": ["model=A", "model=B"],
      "billingInformation": "https://learn.microsoft.com/en-us/graph/teams-licenses",
      "registrationForm": "https://aka.ms/teamsgraph/requestaccess"
    },
    {
      "urlsToWatch": [
        "https://graph.microsoft.com/*/teams/*/channels/*/messages/*",
        "https://graph.microsoft.com/*/teams/*/channels/*/messages/*/replies/*",
        "https://graph.microsoft.com/*/chats/*/messages/{message-id}*"
      ],
      "httpVerbs": ["PATCH"],
      "supportedPaymentModels": ["model=A"],
      "billingInformation": "https://learn.microsoft.com/en-us/graph/teams-licenses",
      "registrationForm": "https://aka.ms/teamsgraph/requestaccess"
    },
    {
      "urlsToWatch": [
        "https://graph.microsoft.com/v1.0/*/assignSensitivityLabel",
        "https://graph.microsoft.com/beta/*/assignSensitivityLabel"
      ],
      "httpVerbs": ["POST"],
      "registrationForm": "https://aka.ms/PreviewSPOPremiumAPI"
    }
  ]
}
```
