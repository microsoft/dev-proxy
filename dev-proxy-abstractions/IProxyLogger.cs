// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Titanium.Web.Proxy.EventArguments;

namespace Microsoft.DevProxy.Abstractions;

public enum MessageType
{
    Normal,
    InterceptedRequest,
    PassedThrough,
    Warning,
    Tip,
    Failed,
    Chaos,
    Mocked,
    InterceptedResponse,
    FinishedProcessingRequest,
    Skipped
}

public class LoggingContext(SessionEventArgs session)
{
    public SessionEventArgs Session { get; } = session;
}