// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions.LanguageModel;

public interface ILanguageModelClient
{
    Task<ILanguageModelCompletionResponse?> GenerateChatCompletionAsync(ILanguageModelChatCompletionMessage[] messages);
    Task<ILanguageModelCompletionResponse?> GenerateCompletionAsync(string prompt);
    Task<bool> IsEnabledAsync();
}