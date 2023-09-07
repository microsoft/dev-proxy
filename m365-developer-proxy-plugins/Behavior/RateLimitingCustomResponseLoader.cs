// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft365.DeveloperProxy.Abstractions;
using Microsoft365.DeveloperProxy.Plugins.MockResponses;
using System.Text.Json;

namespace Microsoft365.DeveloperProxy.Plugins.Behavior;

internal class RateLimitingCustomResponseLoader : IDisposable
{
    private readonly ILogger _logger;
    private readonly RateLimitConfiguration _configuration;

    public RateLimitingCustomResponseLoader(ILogger logger, RateLimitConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    private string _customResponseFilePath => Path.Combine(Directory.GetCurrentDirectory(), _configuration.CustomResponseFile);
    private FileSystemWatcher? _watcher;

    public void LoadResponse()
    {
        if (!File.Exists(_customResponseFilePath))
        {
            _logger.LogWarn($"File {_configuration.CustomResponseFile} not found. No response will be provided");
            return;
        }

        try
        {
            using (FileStream stream = new FileStream(_customResponseFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    var responseString = reader.ReadToEnd();
                    var response = JsonSerializer.Deserialize<MockResponse>(responseString);
                    if (response is not null)
                    {
                        _configuration.CustomResponse = response;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"An error has occurred while reading {_configuration.CustomResponseFile}:");
            _logger.LogError(ex.Message);
        }
    }

    public void InitResponsesWatcher()
    {
        if (_watcher is not null)
        {
            return;
        }

        string path = Path.GetDirectoryName(_customResponseFilePath) ?? throw new InvalidOperationException($"{_customResponseFilePath} is an invalid path");
        if (!File.Exists(_customResponseFilePath))
        {
            _logger.LogWarn($"File {_configuration.CustomResponseFile} not found. No mocks will be provided");
            return;
        }

        _watcher = new FileSystemWatcher(Path.GetFullPath(path))
        {
            NotifyFilter = NotifyFilters.CreationTime
                             | NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size,
            Filter = Path.GetFileName(_customResponseFilePath)
        };
        _watcher.Changed += ResponseFile_Changed;
        _watcher.Created += ResponseFile_Changed;
        _watcher.Deleted += ResponseFile_Changed;
        _watcher.Renamed += ResponseFile_Changed;
        _watcher.EnableRaisingEvents = true;

        LoadResponse();
    }

    private void ResponseFile_Changed(object sender, FileSystemEventArgs e)
    {
        LoadResponse();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
