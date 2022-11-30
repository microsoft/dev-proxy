<h1 align="center">
    <img alt="Microsoft Graph Developer Proxy" src="./samples/img/graph.png" height="78" />
  <br>Microsoft Graph Developer Proxy<br>
</h1>

<h4 align="center">
  Build with Microsoft Graph. Reliably 
</h4>

<p align="center">
  <a href="#get-started">Get started</a> |
  <a href="#example-usage">Example usage</a> |
  <a href="#features">Features</a> |
  <a href="#trademarks">Trademarks</a>
</p>

<p align="center">
<video src="https://user-images.githubusercontent.com/11563347/204810331-8479815d-0d69-4793-aea6-fed737b7d15c.mp4" data-canonical-src="https://user-images.githubusercontent.com/11563347/204810331-8479815d-0d69-4793-aea6-fed737b7d15c.mp4" controls="controls" muted="muted" class="d-block rounded-bottom-2 border-top width-fit" style="max-height:640px;" autoplay>
  </video>
</p>

Microsoft Graph Developer Proxy is a command line tool that simulates real world behaviours of Microsoft Graph, locally.

Microsoft Graph Developer Proxy aims to provide a better way to test applications that use Microsoft Graph. Using the proxy to simulate errors, mock responses and demonstrate behaviours like throttling, developers can identify and fix issues in their code early in the development cycle before they reach production.

## Get started

If you are new to Microsoft Graph Developer Proxy, we highly recommend that you begin with our [tutorial](/wiki/Get-started) which will guide you through the installation process and running the proxy for the first time.

## Example usage

Start the proxy on port 8080, set the chance for a request to Microsoft Graph to fail with an HTTP status code of either 429 or 503 at 50%, and ignore any mock responses that may have been provided, execute:

```
msgraph-developer-proxy --port 8080 --failure-rate 50 --no-mocks --allowed-errors 429 503
```

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

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft trademarks or logos is subject to and must follow [Microsoft’s Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general). Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship. Any use of third-party trademarks or logos are subject to those third-party’s policies.

## A Microsoft Hackathon 2022 Project

The initial build of this project was completed in the week of 5-9 September 2022 by Waldek Mastykarz, Gavin Barron and Garry Trinder
