// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions;

public interface ILanguageModelClient
{
    Task<ILanguageModelCompletionResponse?> GenerateChatCompletion(ILanguageModelChatCompletionMessage[] messages);
    Task<ILanguageModelCompletionResponse?> GenerateCompletion(string prompt);
    Task<bool> IsEnabled();
}