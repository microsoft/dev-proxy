# Support for any URLs via the Microsoft Graph Developer Proxy

Some applications are using an hybrid approach when it comes to consuming APIs. Some APIs are available on Microsoft Graph while some are not. To enable developers to fully test their scenarios with the Microsoft Graph Developer Proxy, the proxy should support acting as a reverse proxy for more than just one URL at a time, and include URLs that are outside of the Microsoft Graph. This can also be used by internal teams to test their APIs on internal endpoints.

## History

| Version | Date | Comments | Author |
| ------- | -------- | ----- | --- |
| 1.0 | 2022-12-09 | Initial specifications | @sebastienlevert


## Configuration file

A configuration file should be available for developers who want to support scenarios outside of Microsoft Graph. The file should be at the root of the proxy working folder and named `config.json`. This file should have default RegEx values for all supported Microsoft Graph clouds and SharePoint URLs.

This configuration can also be extended by "any" URLs to support 1P endpoints, vanity URLs, custom services, etc.

| Property | Type | Comments |
| ------- | -------- | ----- |
| id | String (unique) | This can be any string value but needs to be unique |
| regEx | String | String value of the RegEx to use |
| isWildcard | Boolean | If the value is `true`, the RegEx gets an appended wildcard token. If `false`, only the RegEx value should be considered |

> **Note**
> These RegEx need to be validated in the context of the App

```json
{
  "endpoints": [
    { 
      "id": "microsoftGraph",
      "regEx": "https:\/\/(canary.)?((graph|dod-graph).microsoft.(com|us)|microsoftgraph.chinacloudapi.cn)",
      "isWildcard": true
    },
    {
      "id": "sharePoint",
      "regEx": "https:\/\/(.*).sharepoint(-df)?.(com|us)",
      "isWildcard": true
    },
    {
      "id": "customService",
      "regEx": "https:\/\/customService.azurewebsites.net",
      "isWildcard": true
    },
    {
      "id": "customService2",
      "regEx": "https:\/\/customService2.azurewebsites.net/api/GetMyData",
      "isWildcard": false
    }
  ]
    
  }
}
```

## Remove the `--cloud` option on the Proxy

This new capability renders the `--cloud` option obsolete on the Proxy. We should deprecate the capability, remove associated documentation and once the Proxy hits its next major version, introduce the breaking change. Until then, if the option is used, notify the developer that this option now is obsolete and will use the `config.json` file as its source of truth for URLs it should proxy.

## Mocking responses

Responses can be mocked and should be updated to include either the absolute URL for the response to be returned or a RegEx. This RegEx would be used in scenarios where a developer want to test the same set of responses on multiple clouds or on different environments. Is is possible to use the `id` property of the endpoint to reuse the base RegEx with using `{valueOfTheId}` and append paths.

```json
{
  "responses": [
    {
      "url": "https://graph.microsoft.com/v1.0/me",
      "responseBody": {
        "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users/$entity",
        "businessPhones": [
          "+1 412 555 0109"
        ],
        "displayName": "Megan Bowen",
        "givenName": "Megan",
        "jobTitle": "Auditor",
        "mail": "MeganB@M365x214355.onmicrosoft.com",
        "mobilePhone": null,
        "officeLocation": "12/1110",
        "preferredLanguage": "en-US",
        "surname": "Bowen",
        "userPrincipalName": "MeganB@M365x214355.onmicrosoft.com",
        "id": "48d31887-5fad-4d73-a9f5-3c356e68a038"
      }
    },
    {
      "url": "{microsoftGraph}/v1.0/me/directReports",
      "responseBody": {
        "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#directoryObjects",
        "value": [
          {
            "@odata.type": "#microsoft.graph.user",
            "id": "ec63c778-24e1-4240-bea3-d12a167d5232",
            "businessPhones": [
                "+20 255501070"
            ],
            "displayName": "Pradeep Gupta",
            "givenName": "Pradeep",
            "jobTitle": "Accountant II",
            "mail": "PradeepG@M365x214355.onmicrosoft.com",
            "mobilePhone": null,
            "officeLocation": "98/2202",
            "preferredLanguage": "en-US",
            "surname": "Gupta",
            "userPrincipalName": "PradeepG@M365x214355.onmicrosoft.com"
          }
        ]
      }
    },
  ]
}
```