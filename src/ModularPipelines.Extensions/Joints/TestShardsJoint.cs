using Joinery.Dovetail;
using Joinery.Dovetail.Attributes;
using Joinery.Dovetail.Joints;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

namespace Rocket.Surgery.ModularPipelines.Extensions.Joints;

/// <summary>Applies <see cref="TestShards.AddRocketTestShards" />: adds the generated test shards, when the build host has any.</summary>
[DovetailExport]
public sealed class TestShardsJoint : IPipelineBuilderJoint
{
    /// <inheritdoc />
    public void Register(IDovetailContext context, PipelineBuilder builder) => builder.AddRocketTestShards();
}
