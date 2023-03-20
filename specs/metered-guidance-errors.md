# Metered APIs guidance and random errors

Using Metered plugin, developers can ensure are aware and utilize the Metered APIs in the most efficient way possible.

Metered APIs allows you to get access to advanced capabilities in Microsoft Graph. They require an active Azure subscription with the calling application. Some of these metered APIs are protected and require additional validation beyond permissions and admin consent. The main goal of this guidance is to know that the APIs they are calling would require extra action when deployed in production and could incur a cost.

## History

| Version | Date | Comments | Author |
| ------- | ---- | -------- | ------ |
| 1.0 | 2023-03-20 | Initial specifications | @sebastienlevert |

## Configuration

N/A

## Implementation

> **Note**
> This metered APIs plugin is only valid for Microsoft Graph requests.  

### Guidance
Metered APIs guidance will be implemented as a plugin, subscribed to one events: `BeforeRequest`.

In the `BeforeRequest` event, the plugin will check if the request URL is part of the identified metered APIs. This list of APIs is documented in the official Microsoft Graph Docs [here](https://learn.microsoft.com/en-us/graph/metered-api-list?view=graph-rest-1.0). This list will likely grow but as a first step, we will be using a static configuration file to identify the endpoints to mark as metered. If the request is targeted at a metered API, we will emit a log message that will include text telling the developer the endpoint is metered and will include a link to the Metered APIs and services in Microsoft Graph documentation: `https://learn.microsoft.com/en-us/graph/metered-api-list`.

```shell
[ REQUEST ]    GET hhttps://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages
[   API   ]  ┌ Passed through
             └ GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages
[ WARNING ]  ┌ This is a metered API.
             │ More info at https://learn.microsoft.com/en-us/graph/teams-licenses
             └ GET https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages 
```

### Random Errors
When registered, the plugin should register 3 new errors for the `GraphRandomErrorPlugin` (The list is coming from https://learn.microsoft.com/en-us/graph/teams-licenses#payment-related-errors):

| Error code | Scenario | Sample error message |
|:-----------|:-----------|:-----------------|
| 402 (Payment Required) | Passing `model=A` without a Microsoft E5 license |`...needs a valid license to access this API...`, `...tenant needs a valid license to access this API...`|
| 402 (Payment Required) | Calling Patch API passing `model=B` |`...query parameter 'model' does not support value 'B' for this API. Use billing model 'A'...`|
| 402 (Payment Required) | `Evaluation mode` capacity exceeded |`...evaluation mode capacity has been exceeded. Use a valid billing model...`|

## Configuration file

The plugin will have its own section in the `appSettings.json` configuration file where metered APIs will be listed. This file should have default values for all supported Microsoft Graph clouds.

```json
{
  "name": "GraphMeteredGuidancePlugin",
  "enabled": true,
  "pluginPath": "GraphProxyPlugins\\msgraph-developer-proxy-plugins.dll",      
  "meteredApis": [
    {
      "urlsToWatch": [
        "https://graph.microsoft.com/v1.0/users/*/chats/getAllMessages",
        "https://graph.microsoft.com/beta/users/*/chats/getAllMessages",
      ],
      "supportedPaymentModels": [
        "model=A",
        "model=B"
      ],
      "documentationUrl": "",
      "billingInformation": "https://learn.microsoft.com/en-us/graph/teams-licenses",
      "registrationForm": "https://aka.ms/teamsgraph/requestaccess"
    },
    {
      "urlsToWatch": [
        "https://graph.microsoft.com/v1.0/teams/*/channels/getAllMessages",
        "https://graph.microsoft.com/beta/teams/*/channels/getAllMessages",
      ],
      "supportedPaymentModels": [
        "model=A",
        "model=B"
      ],
      "billingInformation": "https://learn.microsoft.com/en-us/graph/teams-licenses",
      "registrationForm": "https://aka.ms/teamsgraph/requestaccess"
    },
    {
      "urlsToWatch": [
        "https://graph.microsoft.com/v1.0/teams/*/channels/*/messages/*",
        "https://graph.microsoft.com/beta/teams/*/channels/*/messages/*",
        "https://graph.microsoft.com/v1.0/teams/*/channels/*/messages/*/replies/*",
        "https://graph.microsoft.com/beta/teams/*/channels/*/messages/*/replies/*",
        "https://graph.microsoft.com/v1.0/chats/*/messages/{message-id}*",
        "https://graph.microsoft.com/beta/chats/*/messages/{message-id}*",
      ],
      "httpVerbs": [
        "PATCH"
      ],
      "supportedPaymentModels": [
        "model=A"
      ],
      "billingInformation": "https://learn.microsoft.com/en-us/graph/teams-licenses",
      "registrationForm": "https://aka.ms/teamsgraph/requestaccess"
    },
    {
      "urlsToWatch": [
        "https://graph.microsoft.com/v1.0/*/assignSensitivityLabel",
        "https://graph.microsoft.com/beta/*/assignSensitivityLabel",
      ],
      "httpVerbs": [
        "POST"
      ],
      "billingInformation": "",
      "registrationForm": "https://aka.ms/PreviewSPOPremiumAPI"
    }
  ]
}
```