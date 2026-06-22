using ModularPipelines.Options;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

public interface ICommandInterceptor
{
    ValueTask<(CommandLineToolOptions Options, CommandExecutionOptions? ExecutionOptions)> InterceptAsync(
        CommandLineToolOptions options,
        CommandExecutionOptions? executionOptions = null,
        CancellationToken cancellationToken = default
    );
}

public interface ICommandInterceptor<TOptions> where TOptions : CommandLineToolOptions
{
    ValueTask<(TOptions Options, CommandExecutionOptions? ExecutionOptions)> InterceptAsync(TOptions options, CommandExecutionOptions? executionOptions, CancellationToken cancellationToken);
}
