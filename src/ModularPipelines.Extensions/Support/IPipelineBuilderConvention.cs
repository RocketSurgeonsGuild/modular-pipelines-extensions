namespace Rocket.Surgery.ModularPipelines.Extensions.Support;

/// <summary>
///     IPipelineBuilderConvention
///     Implements the <see cref="IConvention" />
/// </summary>
/// <seealso cref="IConvention" />
public interface IPipelineBuilderConvention : IConvention
{
    /// <summary>
    ///     Register additional logging providers with the logging builder
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="builder"></param>
    void Register(IConventionContext context, PipelineBuilder builder);
}
