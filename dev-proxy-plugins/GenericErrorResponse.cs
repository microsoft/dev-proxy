// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;

namespace Microsoft.DevProxy.Plugins;

public class GenericErrorResponse
{
    public int StatusCode { get; set; }
    public List<MockResponseHeader>? Headers { get; set; }
    public dynamic? Body { get; set; }
}