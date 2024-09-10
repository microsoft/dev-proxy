// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Logging;

#pragma warning disable IDE0130
namespace Microsoft.Extensions.Logging;
#pragma warning restore IDE0130

public static class ILoggerBuilderExtensions
{
    public static ILoggingBuilder AddRequestLogger(this ILoggingBuilder builder, PluginEvents pluginEvents)
    {
        builder.Services.AddSingleton<ILoggerProvider, RequestLoggerProvider>(provider =>
        {
            return new RequestLoggerProvider(pluginEvents);
        });

        return builder;
    }
}
