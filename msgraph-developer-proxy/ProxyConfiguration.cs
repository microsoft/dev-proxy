// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Graph.DeveloperProxy {
    public class ProxyConfiguration : IDisposable
    {
        [JsonPropertyName("port")]
        public int Port { get; set; } = 8000;

        [JsonPropertyName("responses")]
        public IEnumerable<ProxyMockResponse> Responses { get; set; } = Array.Empty<ProxyMockResponse>();

        private readonly string _responsesFilePath;
        private FileSystemWatcher? _watcher;

        public ProxyConfiguration()
        {
            _responsesFilePath = Path.Combine(Directory.GetCurrentDirectory(), "responses.json");
        }

        public void LoadResponses()
        {
            if (!File.Exists(_responsesFilePath))
            {
                Responses = Array.Empty<ProxyMockResponse>();
                return;
            }

            try
            {
                var responsesString = File.ReadAllText(_responsesFilePath);
                var responsesConfig = JsonSerializer.Deserialize<ProxyConfiguration>(responsesString);
                IEnumerable<ProxyMockResponse>? configResponses = responsesConfig?.Responses;
                if (configResponses is not null) {
                    Responses = configResponses;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An error has occurred while reading responses.json:");
                Console.Error.WriteLine(ex.Message);
            }
        }

        public void InitResponsesWatcher()
        {
            if (_watcher is not null)
            {
                return;
            }

            string? path = Path.GetDirectoryName(_responsesFilePath);
            if (path is null)
            {
                throw new InvalidOperationException($"{_responsesFilePath} is an invalid path");
            }
            _watcher = new FileSystemWatcher(path);
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

        private void ResponsesFile_Changed(object sender, FileSystemEventArgs e)
        {
            LoadResponses();
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }

    public class ProxyMockResponse {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
        [JsonPropertyName("method")]
        public string Method { get; set; } = "GET";
        [JsonPropertyName("responseCode")]
        public int? ResponseCode { get; set; } = 200;
        [JsonPropertyName("responseBody")]
        public dynamic? ResponseBody { get; set; }
        [JsonPropertyName("responseHeaders")]
        public IDictionary<string, string>? ResponseHeaders { get; set; }
    }
}
