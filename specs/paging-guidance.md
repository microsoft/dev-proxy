# "Is my app correctly supporting paging" guidance

Using the paging guidance plugin, developers can ensure that they properly handle retrieving multiple pages of data in their applications.

## History

| Version | Date | Comments | Author |
| ------- | ---- | -------- | ------ |
| 1.0 | 2023-02-01 | Initial specifications | @waldekmastykarz |

## Implementation

Paging guidance will be implemented as a plugin, subscribed to two events: `BeforeRequest` and `AfterResponse`.

Through the `AfterResponse` event, the plugin will get access to the API response. In the API response, the plugin will look for the value of the `@odata.nextLink` property for JSON output, and `href` attribute value from the `<link rel="next" />` tag for XML output. If the value is present, the plugin will store it for later use.

In the `BeforeRequest` event, the plugin will check if the request URL uses either the `$skip` or `$skiptoken` query parameters. If it does, the plugin will check if the URL matches one of the URLs it stored previously. If it doesn't, the plugin will log a warning that the application is possibly building its own paging URLs and could likely retrieve incomplete data. If the request is a Graph request, the log message will also include a link to the paging guidance documentation: `https://learn.microsoft.com/graph/paging`.

This is a generic OData plugin not specific to Microsoft Graph.
