# Rate limiting guidance

Using the rate limiting guidance plugin, developers can ensure that they properly handle rate limiting in their applications.

Rate limiting is an API feature that communicates to API consumers how many resources are left in a given time window. When the API consumer reaches the limit, the API will return a `429 Too Many Requests` response. API consumers can use information about rate limiting to avoid reaching the limit and to build applications that can stay under the throttling limit.

## History

| Version | Date | Comments | Author |
| ------- | ---- | -------- | ------ |
| 1.0 | 2023-03-08 | Initial specifications | @waldekmastykarz |

## Configuration

Setting|Description
-------|-----------
`headerLimit`|Name of the `RateLimit-Limit` header returned by the API. Default: `RateLimit-Limit`
`headerRemaining`|Name of the `RateLimit-Remaining` header returned by the API. Default: `RateLimit-Remaining`
`headerReset`|Name of the `RateLimit-Reset` header returned by the API. Default: `RateLimit-Reset`
`headerRetryAfter`|Name of the `Retry-After` header returned by the API. Default: `Retry-After`
`costPerRequest`|Number of resources consumed by a single request. Default: `2`
`resetTimeWindowSeconds`|Number of seconds before the resource counter is reset. Default: `60`
`warningThresholdPercent`|Percentage of remaining resources at which the warning should be displayed. Default: `80`
`rateLimit`|Number of resources available in the time window. Default: `120`
`retryAfterSeconds`|Number of seconds to wait before retrying the request. Default: `5`

## Implementation

Rate limiting guidance will be implemented as a plugin, subscribed to the `BeforeRequest` event.

The plugin starts with the total number of resources available and the time of next reset as configured in the `rateLimit` and `resetTimeWindowSeconds` settings.

On each intercepted request, the plugin will subtract the value of the `costPerRequest` setting from the total number of resources available and pass the request through. When the number of resources left until the next reset drops below the percentage configured in the `warningThresholdPercent` setting, the plugin will still pass the request through but add to the response Rate Limiting headers using the names configured in the `headerLimit`, `headerRemaining`, and `headerReset` settings.

When the number of resources left drops to zero, the plugin will start returning `429 Too Many Requests` response and log a FAIL request log message (the application failed to back off in time to avoid throttling). The plugin will also return the number of seconds to wait before retrying the request as configured in the `retryAfterSeconds` setting. This information will be sent in the header configured in the `headerRetryAfter` setting.

If the application tries calling the API again before the time specified in the `Retry-After` header, the API will return `429 Too Many Requests` again and will log a FAIL request log message (the application failed to wait the specified amount of time before calling the API again).

When the time of next reset is reached, the plugin will reset the total number of resources available to the value of the `rateLimit` setting.

Rate limiting and retry after headers are configurable because some APIs use different names for these headers, such as `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`, and `X-Retry-After`. Default names for these headers are aligned with the IETF spec and Microsoft Graph.

In the documentation, include a reference to [throttling documentation](https://learn.microsoft.com/sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online#application-throttling) so that customers know how to configure settings to simulate different scenarios.
