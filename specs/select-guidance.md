# $select guidance

The `$select` guidance helps developers improve the performance of their apps, by suggesting the use of the `$select` query string parameter where relevant and not already used.

## History

| Version | Date | Comments | Author |
| ------- | ---- | -------- | ------ |
| 1.0 | 2023-07-12 | Initial specifications | @waldekmastykarz |

## Requirements

Not all Microsoft Graph endpoints support the `$select` parameter, so for the select guidance to be useful, it needs to be accurate and avoid false positives. To achieve this, we need an authoritative source of information about which Microsoft Graph endpoints support the use of `$select`.

## Options

We've got two options to address this issue:

1. Parse information about supported parameters from https://github.com/microsoftgraph/msgraph-metadata/tree/master/openapi/v1.0
1. Use the https://graphexplorerapi.azurewebsites.net/openapi API to get the information about supported parameters for a specific Graph endpoint

We decided to go try option 1 to start with. We believe, that it will allow us to ensure that the Proxy can handle the throughput of requests. Because the information about supported parameters is readily available to us, rather than having to call the API for each intercepted requests, we'll be able to process each request faster. By regularly refreshing the downloaded Open API file, we can ensure that the data that we've got locally is fresh and representative of what's available on Microsoft Graph.

## Implementation

We're going to use the information from https://github.com/microsoftgraph/msgraph-metadata/blob/master/openapi/v1.0/openapi.yaml (and its beta equivalent) as the authoritative source of support for `$select` on Microsoft Graph.

When Proxy starts, the Select Guidance Plugin will check if it should update the Open API files. It will do so, by comparing the current date with the last modified date of the locally stored file. If the file modified date is different than today, the plugin will download new files from GitHub and update their contents.

The plugin will download files asynchronously, so that downloading doesn't block the Proxy from working. When downloading completes, the Select Guidance Plugin will parse their contents and refresh its in-memory representation so that the new contents can be used immediately.

To speed up first-time use, we'll include the v1.0 and beta Open API files for Graph together with the Proxy. We'll update them before each release so that they're as recent as possible.

When the Select Guidance Plugin is initialized, it will use the OpenAPI.NET SDK to parse the Open API files (v1.0 and beta) and create an in-memory representation of their contents. It will use these models to detect if a specific Graph endpoint supports `$select` or not.

When the Select Guidance Plugin gets an intercepted request, it will check if the intercepted request is a Microsoft Graph request. If it is, it will tokenize it using the same logic as the Minimal Permissions plugin, and use the tokenized path to find the corresponding path in the Open API Graph model in memory, and check if the endpoint supports `$select` or not.

If the endpoint of the intercepted request supports `$select`, and `$select` is not present on the request, the Select Guidance Plugin will display a warning.
