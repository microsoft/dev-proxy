// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions;

public class LanguageModelConfiguration
{
    public bool Enabled { get; set; } = false;
    // default Ollama URL
    public string? Url { get; set; } = "http://localhost:11434";
    public string? Model { get; set; } = "phi3";
    public bool CacheResponses { get; set; } = true;
}