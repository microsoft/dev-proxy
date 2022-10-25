using System.Text.Json.Serialization;

namespace Microsoft.Graph.DeveloperProxy {
    internal class ErrorResponseBody {
        [JsonPropertyName("error")]
        public ErrorResponseError Error { get; set; }

        public ErrorResponseBody(ErrorResponseError error) {
            Error = error;
        }
    }

    internal class ErrorResponseError {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        [JsonPropertyName("innerError")]
        public ErrorResponseInnerError? InnerError { get; set; }
    }

    internal class ErrorResponseInnerError {
        [JsonPropertyName("request-id")]
        public string RequestId { get; set; } = string.Empty;
        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;
    }
}
