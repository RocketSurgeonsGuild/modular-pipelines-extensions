using ModularPipelines.Options;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

namespace Rocket.Surgery.ModularPipelines.Extensions.Conventions;

internal class SolutionInterceptor(SolutionSettings? settings = null) :
    ICommandInterceptor<DotNetBuildOptions>,
    ICommandInterceptor<DotNetTestOptions>
{
    public ValueTask<(DotNetBuildOptions Options, CommandExecutionOptions? ExecutionOptions)> InterceptAsync(
        DotNetBuildOptions options,
        CommandExecutionOptions? executionOptions,
        CancellationToken cancellationToken
    )
    {
        if (settings is { Configuration: { } configuration })
            options.Configuration ??= settings.Configuration;
        return ValueTask.FromResult((options, executionOptions));
    }

    public ValueTask<(DotNetTestOptions Options, CommandExecutionOptions? ExecutionOptions)> InterceptAsync(
        DotNetTestOptions options,
        CommandExecutionOptions? executionOptions,
        CancellationToken cancellationToken
    )
    {
        if (settings is { Configuration: { } configuration })
            options.Configuration ??= settings.Configuration;
        return ValueTask.FromResult((options, executionOptions));
    }
}
