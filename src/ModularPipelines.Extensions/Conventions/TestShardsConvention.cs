using Rocket.Surgery.ModularPipelines.Extensions.Modules;
using Rocket.Surgery.ModularPipelines.Extensions.Support;

namespace Rocket.Surgery.ModularPipelines.Extensions.Conventions;

/// <summary>Applies <see cref="TestShards.AddRocketTestShards" />: adds the generated test shards, when the build host has any.</summary>
[ExportConvention]
public sealed class TestShardsConvention : IPipelineBuilderConvention
{
    /// <inheritdoc />
    public void Register(IConventionContext context, PipelineBuilder builder) => builder.AddRocketTestShards();
}
