namespace Rocket.Surgery.ModularPipelines.Extensions.Support;

/// <summary>
///     IPipelineBuilderAsyncConvention
///     Implements the <see cref="IConvention" />
/// </summary>
/// <seealso cref="IConvention" />
public interface IPipelineBuilderAsyncConvention : IConvention
{
    /// <summary>
    ///     Register additional logging providers with the logging builder
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="builder"></param>
    /// <param name="cancellationToken"></param>
    ValueTask Register(IConventionContext context, PipelineBuilder builder, CancellationToken cancellationToken);
}
