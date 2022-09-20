using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Graph.ChaosProxy {
    public class ChaosProxyConfiguration: IDisposable {
        [JsonPropertyName("port")]
        public int Port { get; set; } = 8000;
        [JsonPropertyName("failureRate")]
        public int FailureRate { get; set; } = 50;
        [JsonPropertyName("noMocks")]
        public bool NoMocks { get; set; } = false;
        [JsonPropertyName("responses")]
        public IEnumerable<ChaosProxyMockResponse> Responses { get; set; } = new ChaosProxyMockResponse[] { };

        private string _responsesFilePath;
        private FileSystemWatcher _watcher;

        public ChaosProxyConfiguration() {
            _responsesFilePath = Path.Combine(Directory.GetCurrentDirectory(), "responses.json");
        }

        public void LoadResponses() {
            if (!File.Exists(_responsesFilePath)) {
                Responses = new ChaosProxyMockResponse[] { };
                return;
            }

            try {
                var responsesString = File.ReadAllText(_responsesFilePath);
                var responsesConfig = JsonSerializer.Deserialize<ChaosProxyConfiguration>(responsesString);
                Responses = responsesConfig.Responses;
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"An error has occurred while reading responses.json:");
                Console.Error.WriteLine(ex.Message);
            }
        }

        public void InitResponsesWatcher() {
            if (_watcher != null) {
                return;
            }

            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_responsesFilePath));
            _watcher.NotifyFilter = NotifyFilters.CreationTime
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size;
            _watcher.Filter = Path.GetFileName(_responsesFilePath);
            _watcher.Changed += ResponsesFile_Changed;
            _watcher.Created += ResponsesFile_Changed;
            _watcher.Deleted += ResponsesFile_Changed;
            _watcher.Renamed += ResponsesFile_Changed;
            _watcher.EnableRaisingEvents = true;

            LoadResponses();
        }

        private void ResponsesFile_Changed(object sender, FileSystemEventArgs e) {
            LoadResponses();
        }

        public void Dispose() {
            _watcher?.Dispose();
        }
    }

    public class ChaosProxyMockResponse {
        [JsonPropertyName("url")]
        public string Url { get; set; }
        [JsonPropertyName("responseCode")]
        public int? ResponseCode { get; set; } = 200;
        [JsonPropertyName("responseBody")]
        public dynamic? ResponseBody { get; set; }
        [JsonPropertyName("responseHeaders")]
        public IDictionary<string, string>? ResponseHeaders { get; set; }
    }
}
