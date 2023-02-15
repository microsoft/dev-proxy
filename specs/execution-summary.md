# Execution summary

Execution summary gives users a quick overview of the requests intercepted by the proxy and metrics about the number and types of requests messages logged by the proxy. This feature retrieves information about requests from [recorded request logs](./recording-requests.md).

## History

| Version | Date | Comments | Author |
| ------- | ---- | -------- | ------ |
| 1.0 | 2023-02-15 | Initial specifications | @waldekmastykarz |

## Implementation

### Information source

Execution summary is a proxy plugin subscribed to the `RecordingStopped` event emitted by the request logs recorder. This event contains information about the request log messages captured during the recording session. The execution summary plugin uses this information to generate a summary of the requests captured during the recording session. The summary uses Markdown syntax to format the output.

### Configuration

Setting|Option|Description
-------|-----------------|-----------
`filePath`|`--summary-file-path`|Path to the file where the summary should be saved. If not specified, the summary will be printed to the console. Path can be absolute or relative to the current working directory. Default: empty.
`groupBy`|`--summary-group-by`|Specifies how the information should be grouped in the summary. Available options: `url` (default), `messageType`.

### Type of information

Execution summary contains the following information:

- Summary title (fixed)
- Date and time of the recording session
- Groups as specified by the `groupBy` configuration option:
  - if grouped by URL:
    - List of requests captured during the recording session sorted alphabetically. For each request:
      - Request method
      - Request URL
      - List of unique request log messages for this request issued by the proxy, grouped and sorted by severity, along with the number of occurrences. Excluding the `InterceptedRequest` and `PassedThrough` severity.
  - if grouped by message type:
    - List of severities sorted by severity level. For each severity:
      - Severity name
      - List of unique URLs logged for this severity sorted alphabetically, along with the number of occurrences
- Summary
  - Total number of requests captured
  - For each type of request log message:
    - Number of requests with this type of request log message

### Sample summary

#### Grouped by URL

```md
# Microsoft Graph Developer Proxy execution summary

Date: 2023-02-06 12:00:00

## Requests

### GET https://graph.microsoft.com/v1.0/me

#### Warning

- (10) To improve performance of your application, use the $select parameter. ore info at https://learn.microsoft.com/graph/query-parameters#select-parameter

#### Tip

- (22) To handle API errors more easily, use the Graph SDK. More info at https://aka.ms/move-to-graph-js-sdk

#### Failed

- (1) Calling https://graph.microsoft.com/v1.0/me again before waiting for the Retry-After period. Request will be throttled

#### Chaos

- (15) 503 ServiceUnavailable
- (20) 429 TooManyRequests
- (10) 500 InternalServerError

#### Mocked

### GET https://graph.microsoft.com/v1.0/me/users

#### Warning

- (10) To improve performance of your application, use the $select parameter. ore info at https://learn.microsoft.com/graph/query-parameters#select-parameter

#### Tip

- (22) To handle API errors more easily, use the Graph SDK. More info at https://aka.ms/move-to-graph-js-sdk

#### Failed

- (1) Calling https://graph.microsoft.com/v1.0/me/users again before waiting for the Retry-After period. Request will be throttled

#### Chaos

- (15) 503 ServiceUnavailable
- (20) 429 TooManyRequests
- (10) 500 InternalServerError

#### Mocked

## Summary

Category|Count
--------|----:
Requests intercepted|100
Requests passed through|10
Requests with chaos|80
Requests mocked|10
Tips|120
Warnings|30
Failures|5
```

#### Grouped by message type

```md
# Microsoft Graph Developer Proxy execution summary

Date: 2023-02-06 12:00:00

## Message types

### Requests intercepted

- (10) GET https://graph.microsoft.com/v1.0/me
- (30) GET https://graph.microsoft.com/v1.0/me/messages
- (12) GET https://graph.microsoft.com/v1.0/me/users

### Requests passed through

- (10) GET https://graph.microsoft.com/v1.0/me

### Requests with chaos

#### 429 TooManyRequests

- (64) GET https://graph.microsoft.com/v1.0/me
- (32) GET https://graph.microsoft.com/v1.0/me/users
  
#### 500 InternalServerError

- (33) GET https://graph.microsoft.com/v1.0/me

### Requests mocked

#### https://graph.microsoft.com/v1.0/me/*

- (23) GET https://graph.microsoft.com/v1.0/me/messages

### Tips

#### To handle API errors more easily, use the Graph SDK. More info at https://aka.ms/move-to-graph-js-sdk

- (14) GET https://graph.microsoft.com/v1.0/me

### Warnings

#### To improve performance of your application, use the $select parameter. ore info at https://learn.microsoft.com/graph/query-parameters#select-parameter

- (23) GET https://graph.microsoft.com/v1.0/me

### Failures

#### Calling https://graph.microsoft.com/v1.0/me again before waiting for the Retry-After period. Request will be throttled

- (33) GET https://graph.microsoft.com/v1.0/me

## Summary

Category|Count
--------|----:
Requests intercepted|100
Requests passed through|10
Requests with chaos|80
Requests mocked|10
Tips|120
Warnings|30
Failures|5
```
