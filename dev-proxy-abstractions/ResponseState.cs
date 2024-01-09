// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions;

public class ResponseState
{
    /// <summary>
    /// Should be set to true when an event handler for a BeforeRequest event sets a response.
    /// If true no other plugin handling the BeforeRequest event should set a response.
    /// </summary>
    public bool HasBeenSet { get; set; } = false;
    /// <summary>
    /// Should be set to true when and event handler for a BeforeResponse event modifies a response.
    /// If true caution should be used when making further modifications to the response as unintended consequences may arise
    /// </summary>
    public bool HasBeenModified { get; set; } = false;
}
