using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.ModularPipelines.Extensions.Support;

namespace Rocket.Surgery.ModularPipelines.Extensions.Distributed.Conventions;

/// <summary>
///     Applies <see cref="RocketDistributedPipelineBuilderExtensions.AddRocketDistributed(PipelineBuilder, Action{RocketDistributedOptions}?)" />
///     to every pipeline that imports conventions, unless <c>Pipeline:Distributed:Enabled</c> is <see langword="false" /> or it was
///     already called.
/// </summary>
[ExportConvention]
internal sealed class RocketDistributedConvention : IPipelineBuilderConvention
{
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The conventions plugin that runs this convention already requires unreferenced code."
    )]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The conventions plugin that runs this convention already requires dynamic code.")]
    public void Register(IConventionContext context, PipelineBuilder builder)
    {
        if (builder.Services.Any(static d => d.ServiceType == typeof(RocketDistributedRegistration))) return;
        var options = RocketDistributedOptions.FromConfiguration(builder.Configuration);
        if (!options.Enabled) return;
        builder.AddRocketDistributed(options);
    }
}
