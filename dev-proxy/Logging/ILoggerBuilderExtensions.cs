// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DevProxy.Abstractions;
using Microsoft.DevProxy.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Logging;

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
