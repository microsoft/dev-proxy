// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine.Invocation;
using Microsoft.DevProxy.Abstractions;

namespace Microsoft.DevProxy;

public class MSGraphDbCommandHandler : ICommandHandler
{
    private readonly IProxyLogger _logger;

    public MSGraphDbCommandHandler(IProxyLogger logger)
    {
        _logger = logger;
    }

    public int Invoke(InvocationContext context)
    {
        return InvokeAsync(context).GetAwaiter().GetResult();
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        return await MSGraphDbUtils.GenerateMSGraphDb(_logger);
    }
}
