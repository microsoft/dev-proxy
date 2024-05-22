// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;

namespace Microsoft.DevProxy.Abstractions;

public interface IProxyPlugin
{
    string Name { get; }
    Option[] GetOptions();
    Command[] GetCommands();
    void Register();
}
