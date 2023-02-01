# Recording requests

Recording requests allows Developer Proxy to capture requests and related messages that were intercepted by the proxy while in record mode. The captured requests are passed for further processing to the Developer Proxy plugins. This feature is foundational for scenarios such as permission analysis, building tailored SDKs, audit reports, etc.

## History

| Version | Date | Comments | Author |
| ------- | ---- | -------- | ------ |
| 1.0 | 2023-02-01 | Initial specifications | @waldekmastykarz |

## Implementation

Developer Proxy can be put in recording mode either by using the `--record` command line option or by pressing `r` after starting the proxy. Recording can be stopped by pressing `s` while running the proxy or by closing the proxy using `CTRL+C`. While recording is stopped and the proxy is running, users can start a new recording session by pressing `r` again.

While recording, Developer Proxy will show an indicator in the terminal window. The indicator will be displayed in the top right corner of the window and will be hidden if the proxy is not recording.

Proxy is recording requests by subscribing to a `LogRequest` event which is raised each time proxy logs a request-related log message. The event contains information about the logged message.

After the recording is stopped, Developer Proxy will raise the `RecordingStopped` event. Plugins can subscribe to this event to process the captured requests.

Recording is a core feature of Developer Proxy and will be implemented in the main proxy code.
