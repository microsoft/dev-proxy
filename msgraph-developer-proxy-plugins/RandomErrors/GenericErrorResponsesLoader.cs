// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy.Abstractions;
using System.Text.Json;
using System.IO;

namespace Microsoft.Graph.DeveloperProxy.Plugins.RandomErrors;

internal class GenericErrorResponsesLoader : IDisposable {
    private readonly ILogger _logger;
    private readonly GenericRandomErrorConfiguration _configuration;

    public GenericErrorResponsesLoader(ILogger logger, GenericRandomErrorConfiguration configuration) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    private string _errorsFile => Path.Combine(Directory.GetCurrentDirectory(), _configuration.ErrorsFile ?? "");
    private FileSystemWatcher? _watcher;

    public void LoadResponses() {
        if (!File.Exists(_errorsFile)) {
            _logger.LogWarn($"File {_configuration.ErrorsFile} not found in the current directory. No error responses will be loaded");
            _configuration.Responses = Array.Empty<GenericErrorResponse>();
            return;
        }

        try {
            using (FileStream stream = new FileStream(_errorsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                using (StreamReader reader = new StreamReader(stream)) {                var responsesString = reader.ReadToEnd();
                    var responsesConfig = JsonSerializer.Deserialize<GenericRandomErrorConfiguration>(responsesString);
                    IEnumerable<GenericErrorResponse>? configResponses = responsesConfig?.Responses;
                    if (configResponses is not null) {
                        _configuration.Responses = configResponses;
                        _logger.LogInfo($"Error responses for {configResponses.Count()} url patterns loaded from from {_configuration.ErrorsFile}");
                    }
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError($"An error has occurred while reading {_configuration.ErrorsFile}:");
            _logger.LogError(ex.Message);
        }
    }

    public void InitResponsesWatcher() {
        if (_watcher is not null) {
            return;
        }

        string path = Path.GetDirectoryName(_errorsFile) ?? throw new InvalidOperationException($"{_errorsFile} is an invalid path");
        _watcher = new FileSystemWatcher(path);
        _watcher.NotifyFilter = NotifyFilters.CreationTime
                             | NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size;
        _watcher.Filter = Path.GetFileName(_errorsFile);
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
