using System.Text.Json.Serialization;

namespace Microsoft.Graph.ChaosProxy {
    internal class ErrorResponseBody {
        [JsonPropertyName("error")]
        public ErrorResponseError Error { get; set; }
    }

    internal class ErrorResponseError {
        [JsonPropertyName("code")]
        public string Code { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; }
        [JsonPropertyName("innerError")]
        public ErrorResponseInnerError InnerError { get; set; }
    }

    internal class ErrorResponseInnerError {
        [JsonPropertyName("request-id")]
        public string RequestId { get; set; }
        [JsonPropertyName("date")]
        public string Date { get; set; }
    }
}
