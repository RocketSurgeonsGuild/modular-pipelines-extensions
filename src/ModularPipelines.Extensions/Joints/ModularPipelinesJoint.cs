using Joinery.Dovetail;
using Joinery.Dovetail.Attributes;
using Joinery.Dovetail.Joints;
using Microsoft.Extensions.DependencyInjection;

namespace Rocket.Surgery.ModularPipelines.Extensions.Joints;

/// <summary>Sets up what every module of the library relies on: commands run from the repository root.</summary>
[DovetailExport]
internal sealed class ModularPipelinesJoint : IPipelineBuilderJoint
{
    public void Register(IDovetailContext context, PipelineBuilder builder)
    {
        // A factory rather than AddCommandInterceptor<T>: the interceptor needs the pipeline's working directory, which only the builder knows.
        var workingDirectory = builder.WorkingDirectory;
        builder.Services.AddSingleton<ICommandInterceptor>(provider => new RootDirectoryInterceptor(provider.GetRequiredService<SharedSettings>(), workingDirectory));
    }
}
