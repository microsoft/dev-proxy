// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Graph.DeveloperProxy.Abstractions;

public interface ILogger {
    public void Log(string message);
    public void LogWarn(string message);
    public void LogError(string message);
}