// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging.Console;

namespace Microsoft.DevProxy.Logging;

public class ProxyConsoleFormatterOptions: ConsoleFormatterOptions
{
    public bool ShowSkipMessages { get; set; } = true;
}