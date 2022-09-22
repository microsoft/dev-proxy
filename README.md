# Microsoft Graph Chaos Proxy

**Build with Microsoft Graph. Reliably**

Microsoft Graph Chaos Proxy allows you to verify that your application, that uses Microsoft Graph, correctly handles errors that can happen when the application is used at scale. With Graph Chaos Proxy you can:

- **simulate Microsoft Graph API errors**, to verify that your application handles them gracefully when deployed at scale,
- **mock Microsoft Graph API responses**, to use your application with test data

## Background

API errors are hard to replicate because they occur only in specific circumstances, often when the application is under heavy load. Because developers typically work on their own tenants, they can only verify if their application properly handles errors after the application has been deployed to production and put under heavy load. This makes for a poor developer experience.

With Graph Chaos Proxy you can see how your application responds to different errors that could be returned by the API.

You can test your existing applications without any changes, even if you don't have access to their code and they're not using Microsoft Graph SDKs. Graph Chaos Proxy registers itself as a proxy on your machine and intercepts all network traffic. It passes through all requests and only responds to requests to the Microsoft Graph API.

## Features

- run on any OS
  - Windows
  - macOS
  - Linux
- simulate different Microsoft Graph API errors
- verify that your application properly handles throttling
- mock Microsoft Graph API responses
- define wildcard paths to serve mocked responses
- mock responses of different types (JSON, binary, etc.)
- configure proxy to your needs, by setting:
  - failure rate
  - port
  - whether to use mock responses or not
  - which Microsoft Cloud to use (public, DoD, etc.)

## Usage

### Install Graph Chaos Proxy

Extract the archive. Graph Chaos Proxy doesn't need an installation and you can run it from any location on your machine.

> **Tip**
>
> To fully benefit of all features of Graph Chaos Proxy, add its location to the environment path. That way, you'll be able to start it from any directory and will be able to use project-specific mocks.

### First-time use

#### Windows

When started, Graph Chaos Proxy automatically registers itself as a system-wide proxy on your machine.

When you start Graph Chaos Proxy for the first time, you'll be prompted to install a local certificate. This certificate is necessary to decrypt requests to the Microsoft Graph API.

After installing the certificate, you'll be also prompted to allow Graph Chaos Proxy to communicate on your private and public network.

#### macOS

#### Linux

### Use the proxy

After starting the proxy, it will listen to all requests on your machine. When it detects a request to the Microsoft Graph API, it will intercept it. It will pass all other request through to their destination servers.

If you've defined a mock response to the specific request, the proxy will serve the configured response. If there's no matching mock response, or you've temporarily disabled mock responses, the proxy will continue processing the request.

Depending on the configured fail ratio, the proxy will either pass the request through to Microsoft Graph or return one of the relevant network codes with a matching error response.

> **Important**
>
> When closing the proxy, press the Enter key in the proxy's window, so that the proxy unregisters itself from your machine. If you terminate the proxy's process, you will lose network connection. To restore your connection in such case, start the proxy again, and close it by pressing Enter.

### Uninstall Graph Chaos Proxy

Remove the folder with proxy from your disk. Graph Chaos Proxy doesn't create any additional files or registry entries (Windows) on your machine. Remove the certificate installed by Graph Chaos Proxy.

## Configuration

### Mock responses

Graph Chaos Proxy offers you the ability to define mock responses to specific Graph API calls. This capability is invaluable if you want to test your application with specific test cases or want to demonstrate your application with a set of data that is often time-sensitive and inconvenient to manage on an actual Microsoft 365 tenant.

#### Mock responses structure

To define mock responses, create a file named `responses.json` in the current working directory. This allows you to define a specific set of responses for each project that you work with.

Following is a sample `responses.json` file:

```json
{
  "responses": [
    {
      "url": "/v1.0/me",
      "method":  "GET",
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
      },
      "responseHeaders": {
        "content-type": "application/json; odata.metadata=minimal"
      }
    },
    {
      "url": "/v1.0/me/photo",
      "method":  "GET",
      "responseCode": 404
    }
  ]
}
```

The file defines an `responses` property with an array of responses. Each response has the following properties:

Property|Description|Required|Default value|Sample value
--|--|:--:|--|--
`url`|Server-relative URL to a Microsoft Graph API to respond to|yes||`/v1.0/me`
`method`|Http verb used to match request in conjuction with `url`|yes||`GET`
`responseBody`|Body to send as the response to the request|no|_empty_|See above
`responseCode`|Response status code|no|`200`|`404`
`responseHeaders`|Collection of headers to include in the response|no|_empty_|See above

#### Mock responses order

Mocks are matched in the order in which they are defined in the `responses.json` file, first matching response taking precedence over others. If you'd define multiple responses with the same URL and method, the first matching response would be used.

For a configuration file like:

```json
{
  "responses": [
    {
      "url": "/v1.0/me/photo",
      "method":  "GET",
      "responseCode": 500
    },
    {
      "url": "/v1.0/me/photo",
      "method":  "GET",
      "responseCode": 404
    }
  ]
}
```

all `GET` requests to `/v1.0/me/photo` would respond with `500 Internal Server Error`.

> **Important**
>
> The order of mock responses is especially important when working with wildcard URLs.

#### Mock responses to multiple URLs with wildcards

When defining mock responses, you can define a specific URL to mock, but also a URL pattern by replacing part of the URL with an `*` (asterisk), for example:

```json
{
  "responses": [
    {
      "url": "/v1.0/users/*",
      "method":  "GET",
      "responseBody": {
        "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users/$entity",
        "businessPhones": [
          "+1 425 555 0109"
        ],
        "displayName": "Adele Vance",
        "givenName": "Adele",
        "jobTitle": "Product Marketing Manager",
        "mail": "AdeleV@M365x214355.onmicrosoft.com",
        "mobilePhone": null,
        "officeLocation": "18/2111",
        "preferredLanguage": "en-US",
        "surname": "Vance",
        "userPrincipalName": "AdeleV@M365x214355.onmicrosoft.com",
        "id": "87d349ed-44d7-43e1-9a83-5f2406dee5bd"
      }
    }
  ]
}
```

would respond to`GET` requests for `/v1.0/users/bob@contoso.com` and `/v1.0/users/steve@contoso.com` with the same mock response.

If a URL of a mock response contains an `*`, it's used as a regular expression, where each `*` is converted into a `.*`, basically matching any sequence of characters. This is important to keep in mind, because if a pattern is too broad and defined before more specific mocks, it could unintetionally return unexpected responses, for example:

```json
{
  "responses": [
    {
      "url": "/v1.0/users/*",
      "method":  "GET",
      "responseBody": {
        "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users/$entity",
        "businessPhones": [
          "+1 425 555 0109"
        ],
        "displayName": "Adele Vance",
        "givenName": "Adele",
        "jobTitle": "Product Marketing Manager",
        "mail": "AdeleV@M365x214355.onmicrosoft.com",
        "mobilePhone": null,
        "officeLocation": "18/2111",
        "preferredLanguage": "en-US",
        "surname": "Vance",
        "userPrincipalName": "AdeleV@M365x214355.onmicrosoft.com",
        "id": "87d349ed-44d7-43e1-9a83-5f2406dee5bd"
      }
    },
    {
      "url": "/v1.0/users/48d31887-5fad-4d73-a9f5-3c356e68a038",
      "method":  "GET",
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
    }
  ]
}
```

for request `GET /v1.0/users/48d31887-5fad-4d73-a9f5-3c356e68a038`, the proxy would return `Adele Vance` instead of `Megan Bowen`, because the asterisk at the end matches any series of characters. The correct way to define these responses, would be to change their order in the array:

```json
{
  "responses": [
    {
      "url": "/v1.0/users/48d31887-5fad-4d73-a9f5-3c356e68a038",
      "method":  "GET",
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
      "url": "/v1.0/users/*",
      "method":  "GET",
      "responseBody": {
        "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users/$entity",
        "businessPhones": [
          "+1 425 555 0109"
        ],
        "displayName": "Adele Vance",
        "givenName": "Adele",
        "jobTitle": "Product Marketing Manager",
        "mail": "AdeleV@M365x214355.onmicrosoft.com",
        "mobilePhone": null,
        "officeLocation": "18/2111",
        "preferredLanguage": "en-US",
        "surname": "Vance",
        "userPrincipalName": "AdeleV@M365x214355.onmicrosoft.com",
        "id": "87d349ed-44d7-43e1-9a83-5f2406dee5bd"
      }
    }
  ]
}
```

> **Tip**
>
> As a rule of thumb, define the mocks with the longest (most specific) URLs first. Put mocks with shorter URLs and URLs with wildcards (less specific) towards the end of the array.

#### Respond to requests with binary data

For some requests you might want to respond with binary data like documents or images. In Graph Chaos Proxy, you can define a binary response by setting the `responseBody` to a string value that starts with `@` followed by file path relative to the current working directory, for example:

```json
{
  "responses": [
    {
      "url": "/v1.0/users/*/photo/$value",
      "method":  "GET",
      "responseBody": "@picture.jpg",
      "responseHeaders": {
        "content-type": "image/jpeg"
      }
    }
  ]
}
```

When you call `GET /v1.0/users/ben@contoso.com/photo/$value`, you'll get the image stored in the `picture.jpg` file in the current directory.

### Settings

Graph Chaos Proxy comes with several settings that you can use to control how the proxy should run. You can configure these settings by defining them in the `appsettings.json` file in the proxy's installation folder or using options in the command line.

Setting|Description|Command-line option|Allowed values|Default value
--|--|--|--|--
`port`|Port on which the proxy should listen to traffic|`-p, --port`|integer|`8000`
`failureRate`|Rate of requests to Microsoft Graph between `0` and `100` that the proxy should fail. Set to `0` to pass all requests to Microsoft Graph, and to `100` to fail all requests.|`-f, --failure-rate`|`0..100`|`50`
`noMocks`|Don't use mock responses|`--no-mocks`|`true`, `false`|`false`
`allowedErrors`|List of http status code errors the proxy may produce when failing a request|`--allowed-errors -a`|`429 500 502 503 504 507`|`429 500 502 503 504 507`
`cloud`|Which Microsoft Cloud to use for listening to requests|`-c, --cloud`|As defined in the `cloudHosts` setting|`global`
`cloudHosts`|List of Microsoft Clouds allowed for testing||Key-value pairs of name and host name, eg. `"global": "graph.microsoft.com"`|See the `appsettings.json` file

#### Example usage:

```
msgraph-chaos-proxy.exe --port 8080 --failure-rate 50 --no-mocks --allowed-errors 429 503
```

Will configure the proxy listening on port 8080 to fail 50% of requests with an http status code of either 429 or 503 and ignore any mock responses that may have been provided in the `responses.json` file

## Frequently Asked Questions

### Does Graph Chaos Proxy upload any data to Microsoft?

No, it doesn't. While the proxy intercepts all network traffic on your machine, it doesn't upload any data to Microsoft.

### I've got no internet connection after using Graph Chaos Proxy

If you terminate the Graph Chaos Proxy process, the proxy won't be able to unregister itself and you won't have network connection on your machine. To restore network connection, start the proxy and close it by pressing Enter, which will gracefully close the proxy unregistering it on your machine and restoring the regular network connection.

### I keep getting 429 responses

If you have the failure rate at 100% then no request can ever succeed. If you configure a lower failure rate then a previously throttled request will be passed to Microsoft Graph provided the `Retry-After` time period has elapsed.

### I have a 429 response with no `Retry-After` header

As documented in the [Best practices to handle throttling](https://learn.microsoft.com/en-us/graph/throttling#best-practices-to-handle-throttling) an exponential backoff retry policy is recommended.

### All requests fail with gateway timeout

If you're running the proxy for the first time, it can happen that the network access configuration didn't propagate timely and the proxy started without access to your network. Close the proxy by pressing Enter in proxy's window and restart the proxy and it should be working as intended.