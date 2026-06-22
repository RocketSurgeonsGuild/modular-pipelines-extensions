using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModularPipelines.Engine;
using ModularPipelines.Options;

namespace Rocket.Surgery.ModularPipelines.Extensions.Mise;

public partial interface IMise
{
    Task<CommandResult> Execute(
        Span<string> arguments,
        CommandExecutionOptions? executionOptions = null,
        CancellationToken cancellationToken = default
    );

    // Task<CommandResult> Run(
    //     MiseRunOptions options,
    //     CommandExecutionOptions? executionOptions = null,
    //     CancellationToken cancellationToken = default);
}

internal partial class Mise(ICommand command) : IMise
{
    public Task<CommandResult> Execute(Span<string> arguments, CommandExecutionOptions? executionOptions = null, CancellationToken cancellationToken = default)
    {
        return command.ExecuteCommandLineTool(
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
}

[ExcludeFromCodeCoverage]
public record MiseOptions : CommandLineToolOptions { }

/// <summary>
///     Generated extensions for registering mise services.
/// </summary>
public static class MiseExtensions
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void RegisterMiseContext() => ModularPipelinesContextRegistry.RegisterContext(collection => collection.RegisterMiseContext());

    /// <summary>
    ///     Registers mise services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
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
    public static IMise Mise(this IPipelineContext context) => context.Services.Get<IMise>();
}
