// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using System.Text.Json;

namespace Microsoft.DevProxy.Plugins.MockResponses;

internal class CrudApiDefinitionLoader : IDisposable
{
    private readonly ILogger _logger;
    private readonly CrudApiConfiguration _configuration;

    public CrudApiDefinitionLoader(ILogger logger, CrudApiConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    private FileSystemWatcher? _watcher;

    public void LoadApiDefinition()
    {
        if (!File.Exists(_configuration.ApiFile))
        {
            _logger.LogWarn($"File {_configuration.ApiFile} not found. CRUD API will be disabled");
            return;
        }

        try
        {
            using (FileStream stream = new FileStream(_configuration.ApiFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    var apiDefinitionString = reader.ReadToEnd();
                    var apiDefinitionConfig = JsonSerializer.Deserialize<CrudApiConfiguration>(apiDefinitionString);
                    _configuration.BaseUrl = apiDefinitionConfig?.BaseUrl ?? string.Empty;
                    _configuration.DataFile = apiDefinitionConfig?.DataFile ?? string.Empty;

                    IEnumerable<CrudApiAction>? configResponses = apiDefinitionConfig?.Actions;
                    if (configResponses is not null)
                    {
                        _configuration.Actions = configResponses;
                        foreach (var action in _configuration.Actions)
                        {
                            if (string.IsNullOrEmpty(action.Method))
                            {
                                action.Method = action.Action switch
                                {
                                    CrudApiActionType.Create => "POST",
                                    CrudApiActionType.GetAll => "GET",
                                    CrudApiActionType.GetOne => "GET",
                                    CrudApiActionType.Merge => "PATCH",
                                    CrudApiActionType.Update => "PUT",
                                    CrudApiActionType.Delete => "DELETE",
                                    _ => throw new InvalidOperationException($"Unknown action type {action.Action}")
                                };
                            }
                        }
                        _logger.LogInfo($"{configResponses.Count()} actions for CRUD API loaded from {_configuration.ApiFile}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"An error has occurred while reading {_configuration.ApiFile}:");
            _logger.LogError(ex.Message);
        }
    }

    public void InitApiDefinitionWatcher()
    {
        if (_watcher is not null)
        {
            return;
        }

        string path = Path.GetDirectoryName(_configuration.ApiFile) ?? throw new InvalidOperationException($"{_configuration.ApiFile} is an invalid path");
        if (!File.Exists(_configuration.ApiFile))
        {
            _logger.LogWarn($"File {_configuration.ApiFile} not found. No CRUD API will be provided");
            _configuration.Actions = Array.Empty<CrudApiAction>();
            return;
        }

        _watcher = new FileSystemWatcher(Path.GetFullPath(path))
        {
            NotifyFilter = NotifyFilters.CreationTime
                             | NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size,
            Filter = Path.GetFileName(_configuration.ApiFile),
            EnableRaisingEvents = true
        };
        _watcher.Changed += ApiDefinitionFile_Changed;
        _watcher.Created += ApiDefinitionFile_Changed;
        _watcher.Deleted += ApiDefinitionFile_Changed;
        _watcher.Renamed += ApiDefinitionFile_Changed;

        LoadApiDefinition();
    }

    private void ApiDefinitionFile_Changed(object sender, FileSystemEventArgs e)
    {
        LoadApiDefinition();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
