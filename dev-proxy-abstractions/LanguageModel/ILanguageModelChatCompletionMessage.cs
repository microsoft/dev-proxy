// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions;

public interface ILanguageModelChatCompletionMessage
{
    string Content { get; set; }
    string Role { get; set; }
}