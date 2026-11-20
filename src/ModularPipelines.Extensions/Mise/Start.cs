using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModularPipelines.Options;

namespace Rocket.Surgery.ModularPipelines.Extensions.Mise;

/// <summary>Runs <c>mise</c> commands.</summary>
public partial interface IMise
{
    /// <summary>Runs <c>mise exec</c> with the given arguments.</summary>
    /// <param name="arguments">The arguments after <c>exec</c>.</param>
    /// <param name="executionOptions">How to run the command.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The command's result.</returns>
    Task<CommandResult> Execute(
        Span<string> arguments,
        CommandExecutionOptions? executionOptions = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Runs the mise task <paramref name="task" /> (<c>mise run</c>) with the given arguments.</summary>
    /// <param name="task">The task's name.</param>
    /// <param name="arguments">The arguments passed to the task.</param>
    /// <param name="executionOptions">How to run the command.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The command's result.</returns>
    Task<CommandResult> Run(string task, Span<string> arguments,
        CommandExecutionOptions? executionOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the mise task <paramref name="task" /> (<c>mise run</c>).</summary>
    /// <param name="task">The task's name.</param>
    /// <param name="executionOptions">How to run the command.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The command's result.</returns>
    Task<CommandResult> Run(string task,
        CommandExecutionOptions? executionOptions = null,
        CancellationToken cancellationToken = default);
}

internal class Mise(ICommandContext command) : IMise
{
    public Task<CommandResult> Execute(Span<string> arguments, CommandExecutionOptions? executionOptions = null, CancellationToken cancellationToken = default)
    {
        return command.ExecuteCommandLineToolAsync(
            new MiseOptions
            {
                Tool = "mise",
                CommandParts = ["exec"],
                RunSettings = [.. arguments],
            },
            executionOptions,
            cancellationToken
        );
    }

    public Task<CommandResult> Run(string task, Span<string> arguments, CommandExecutionOptions? executionOptions = null, CancellationToken cancellationToken = default)
    {
        return command.ExecuteCommandLineToolAsync(
            new MiseOptions
            {
                Tool = "mise",
                CommandParts = ["run"],
                Arguments = [task],
                RunSettings = [.. arguments],
            },
            executionOptions,
            cancellationToken
        );
    }

    public Task<CommandResult> Run(string task, CommandExecutionOptions? executionOptions = null, CancellationToken cancellationToken = default)
    {
        return command.ExecuteCommandLineToolAsync(
            new MiseOptions
            {
                Tool = "mise",
                CommandParts = ["run"],
                Arguments = [task],
            },
            executionOptions,
            cancellationToken
        );
    }
}

/// <summary>The command line options for a <c>mise</c> command.</summary>
[ExcludeFromCodeCoverage]
public record MiseOptions : CommandLineToolOptions { }

/// <summary>
///     Generated extensions for registering mise services.
/// </summary>
public static class MiseExtensions
{
    /// <summary>
    ///     Registers mise services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    [ModularPipelinesIntegration]
    public static IServiceCollection RegisterMiseContext(this IServiceCollection services)
    {
        services.TryAddScoped<IMise, Mise>();
        return services;
    }

    /// <summary>
    ///     Gets the mise service from the pipeline context.
    /// </summary>
    /// <param name="context">The pipeline context.</param>
    /// <returns>The mise service.</returns>
    public static IMise Mise(this IPipelineContext context) => context.Services.GetRequiredService<IMise>();
}
