# Support for any URLs via the Microsoft Graph Developer Proxy

Some applications are using an hybrid approach when it comes to consuming APIs. Some APIs are available on Microsoft Graph while some are not. To enable developers to fully test their scenarios with the Microsoft Graph Developer Proxy, the proxy should support acting as a reverse proxy for more than just one URL at a time, and include URLs that are outside of the Microsoft Graph. This can also be used by internal teams to test their APIs on internal endpoints.

## History

| Version | Date | Comments | Author |
| ------- | -------- | ----- | --- |
| 1.0 | 2022-12-09 | Initial specifications | @sebastienlevert |


## Configuration file

Reusing the `appSettings.json` configuration file should be possible for developers who want to support scenarios outside of Microsoft Graph. This file should have default values for all supported Microsoft Graph clouds and SharePoint URLs.

This configuration can also be extended by "any" URLs to support 1P endpoints, vanity URLs, custom services, etc.

```json
{
  "hostsToWatch": [
    "graph.microsoft.com",
    "graph.microsoft.us",
    "dod-graph.microsoft.us",
    "microsoftgraph.chinacloudapi.cn",
    "*.sharepoint.*",
    "*.sharepoint-df.*"
    "customService.azurewebsites.net"
  ]
}
```

## Remove the `--cloud` option on the Proxy

This new capability renders the `--cloud` option obsolete on the Proxy. We should remove the capability and its associated documentation.

## Mocking responses

Responses can be mocked and should be updated to include the absolute URL for the response to be returned. 

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
  ]
}
```