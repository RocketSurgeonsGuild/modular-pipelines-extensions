using Joinery.Dovetail;
using Joinery.Dovetail.Attributes;
using Joinery.Dovetail.Joints;
using Rocket.Surgery.ModularPipelines.Extensions.History;

namespace Rocket.Surgery.ModularPipelines.Extensions.Joints;

/// <summary>Applies <see cref="RocketHistoryExtensions.AddRocketHistory" /> from the <c>History</c> configuration section.</summary>
[DovetailExport]
public sealed class RunHistoryJoint : IPipelineBuilderJoint
{
    /// <inheritdoc />
    public void Register(IDovetailContext context, PipelineBuilder builder) => builder.AddRocketHistory();
}
