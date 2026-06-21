using ModularPipelines;

namespace build.library.Conventions;

/// <summary>
///     Delegate HostingConventionAction
/// </summary>
/// <param name="context">The context.</param>
/// <param name="builder">The builder.</param>
/// <param name="cancellationToken">The cancellation token.</param>
public delegate ValueTask PipelineBuilderAsyncConvention(IConventionContext context, PipelineBuilder builder, CancellationToken cancellationToken);
