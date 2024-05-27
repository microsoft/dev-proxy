// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Microsoft.DevProxy.LanguageModel;

public class LanguageModelClient(LanguageModelConfiguration? configuration, ILogger logger) : ILanguageModelClient
{
    private readonly LanguageModelConfiguration? _configuration = configuration;
    private readonly ILogger _logger = logger;
    private bool? _lmAvailable;
    private Dictionary<string, string> _cache = new();

    public async Task<string?> GenerateCompletion(string prompt)
    {
        using var scope = _logger.BeginScope("Language Model");

        if (_configuration == null || !_configuration.Enabled)
        {
            // LM turned off. Nothing to do, nothing to report
            return null;
        }

        if (!_lmAvailable.HasValue)
        {
            if (string.IsNullOrEmpty(_configuration.Url))
            {
                _logger.LogError("URL is not set. Language model will be disabled");
                _lmAvailable = false;
                return null;
            }

            if (string.IsNullOrEmpty(_configuration.Model))
            {
                _logger.LogError("Model is not set. Language model will be disabled");
                _lmAvailable = false;
                return null;
            }

            _logger.LogDebug("Checking availability...");
            _lmAvailable = await IsLmAvailable();

            // we want to log this only once
            if (!_lmAvailable.Value)
            {
                _logger.LogError("{model} at {url} is not available", _configuration.Model, _configuration.Url);
                return null;
            }
        }

        if (!_lmAvailable.Value)
        {
            return null;
        }

        if (_configuration.CacheResponses && _cache.TryGetValue(prompt, out var cachedResponse))
        {
            _logger.LogDebug("Returning cached response for prompt: {prompt}", prompt);
            return cachedResponse;
        }

        var response = await GenerateCompletionInternal(prompt);
        if (response == null)
        {
            return null;
        }
        if (response.Error is not null)
        {
            _logger.LogError(response.Error);
            return null;
        }
        else
        {
            if (_configuration.CacheResponses && response.Response is not null)
            {
                _cache[prompt] = response.Response;
            }

            return response.Response;
        }
    }

    private async Task<LanguageModelResponse?> GenerateCompletionInternal(string prompt)
    {
        Debug.Assert(_configuration != null, "Configuration is null");

        try
        {
            using var client = new HttpClient();
            var url = $"{_configuration.Url}/api/generate";
            _logger.LogDebug("Requesting completion. Prompt: {prompt}", prompt);

            var response = await client.PostAsJsonAsync(url,
                new
                {
                    prompt,
                    model = _configuration.Model,
                    stream = false
                }
            );
            return await response.Content.ReadFromJsonAsync<LanguageModelResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate completion");
            return null;
        }
    }

    private async Task<bool> IsLmAvailable()
    {
        Debug.Assert(_configuration != null, "Configuration is null");

        _logger.LogDebug("Checking LM availability at {url}...", _configuration.Url);

        try
        {
            // check if lm is on
            using var client = new HttpClient();
            var response = await client.GetAsync(_configuration.Url);
            _logger.LogDebug("Response: {response}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var testCompletion = await GenerateCompletionInternal("Are you there? Reply with a yes or no.");
            if (testCompletion?.Error is not null)
            {
                _logger.LogError("Error: {error}", testCompletion.Error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't reach language model at {url}", _configuration.Url);
            return false;
        }
    }
}