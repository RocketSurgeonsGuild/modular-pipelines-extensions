using ModularPipelines;

namespace build.library.Conventions;

/// <summary>
///     Delegate HostingConventionAction
/// </summary>
/// <param name="context">The context.</param>
/// <param name="builder">The builder.</param>
public delegate void PipelineBuilderConvention(IConventionContext context, PipelineBuilder builder);
