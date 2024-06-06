// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions;

public interface ILanguageModelCompletionResponse
{
    string? Error { get; set; }
    string? Response { get; set; }
}