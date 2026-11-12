using Joinery.Dovetail;
using Joinery.Dovetail.Attributes;
using Joinery.Dovetail.Joints;

namespace Rocket.Surgery.ModularPipelines.Extensions.Distributed.Joints;

/// <summary>
///     Applies <see cref="RocketDistributedPipelineBuilderExtensions.AddRocketDistributed(PipelineBuilder, Action{RocketDistributedOptions}?)" />
///     to every pipeline that applies Dovetail's joints, unless <c>Pipeline:Distributed:Enabled</c> is <see langword="false" /> or it was
///     already called.
/// </summary>
[DovetailExport]
internal sealed class RocketDistributedJoint : IPipelineBuilderJoint
{
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Like the ModulesJoint that adds the modules, distributed mode assumes a build host that is not trimmed."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Like the ModulesJoint that adds the modules, distributed mode assumes a build host that is not compiled ahead of time."
    )]
    public void Register(IDovetailContext context, PipelineBuilder builder)
    {
        if (builder.Services.Any(static d => d.ServiceType == typeof(RocketDistributedRegistration))) return;
        var options = RocketDistributedOptions.FromConfiguration(builder.Configuration);
        if (!options.Enabled) return;
        builder.AddRocketDistributed(options);
    }
}
