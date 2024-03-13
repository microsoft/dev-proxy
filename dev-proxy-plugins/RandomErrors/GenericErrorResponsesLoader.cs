// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Microsoft.DevProxy.Plugins.RandomErrors;

internal class GenericErrorResponsesLoader : IDisposable
{
    private readonly IProxyLogger _logger;
    private readonly GenericRandomErrorConfiguration _configuration;

    public GenericErrorResponsesLoader(IProxyLogger logger, GenericRandomErrorConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    private string _errorsFile => Path.Combine(Directory.GetCurrentDirectory(), _configuration.ErrorsFile ?? "");
    private FileSystemWatcher? _watcher;

    public void LoadResponses()
    {
        if (!File.Exists(_errorsFile))
        {
            _logger.LogWarning("File {configurationFile} not found in the current directory. No error responses will be loaded", _configuration.ErrorsFile);
            _configuration.Responses = Array.Empty<GenericErrorResponse>();
            return;
        }

        try
        {
            using (FileStream stream = new FileStream(_errorsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    var responsesString = reader.ReadToEnd();
                    var responsesConfig = JsonSerializer.Deserialize<GenericRandomErrorConfiguration>(responsesString, ProxyUtils.JsonSerializerOptions);
                    IEnumerable<GenericErrorResponse>? configResponses = responsesConfig?.Responses;
                    if (configResponses is not null)
                    {
                        _configuration.Responses = configResponses;
                        _logger.LogInformation("{configResponseCount} error responses loaded from {errorFile}", configResponses.Count(), _configuration.ErrorsFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error has occurred while reading {configurationFile}:", _configuration.ErrorsFile);
        }
    }

    public void InitResponsesWatcher()
    {
        if (_watcher is not null)
        {
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

    private void ResponsesFile_Changed(object sender, FileSystemEventArgs e)
    {
        LoadResponses();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
