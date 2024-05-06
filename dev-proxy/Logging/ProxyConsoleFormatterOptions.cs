// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Microsoft.DevProxy.Logging;

public class ProxyConsoleFormatterOptions : ConsoleFormatterOptions
{
    public LabelMode LabelMode { get; set; } = LabelMode.Text;
}