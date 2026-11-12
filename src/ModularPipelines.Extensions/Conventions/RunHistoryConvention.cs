using Rocket.Surgery.ModularPipelines.Extensions.History;
using Rocket.Surgery.ModularPipelines.Extensions.Support;

namespace Rocket.Surgery.ModularPipelines.Extensions.Conventions;

/// <summary>Applies <see cref="RocketHistoryExtensions.AddRocketHistory" /> from the <c>History</c> configuration section.</summary>
[ExportConvention]
public sealed class RunHistoryConvention : IPipelineBuilderConvention
{
    /// <inheritdoc />
    public void Register(IConventionContext context, PipelineBuilder builder) => builder.AddRocketHistory();
}
