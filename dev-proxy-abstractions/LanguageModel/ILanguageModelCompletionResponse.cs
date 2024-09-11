// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions.LanguageModel;

public interface ILanguageModelCompletionResponse
{
    string? Error { get; set; }
    string? Response { get; set; }
}