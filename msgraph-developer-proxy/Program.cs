// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Graph.DeveloperProxy;
using System.CommandLine;

return await new ProxyHost().GetRootCommand().InvokeAsync(args);