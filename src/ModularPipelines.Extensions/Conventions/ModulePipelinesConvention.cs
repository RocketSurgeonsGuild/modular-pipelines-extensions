using Microsoft.Extensions.DependencyInjection;
using Proto.Promises;
using Rocket.Surgery.ModularPipelines.Extensions.Support;

namespace Rocket.Surgery.ModularPipelines.Extensions.Conventions;

[ExportConvention]
internal class ModulePipelinesConvention : IPipelineBuilderConvention
{
    public void Register(IConventionContext context, PipelineBuilder builder)
    {
        // A factory rather than AddCommandInterceptor<T>: the interceptor needs the pipeline's working directory, which only the builder knows.
        var workingDirectory = builder.WorkingDirectory;
        builder.Services.AddSingleton<ICommandInterceptor>(provider => new RootDirectoryInterceptor(provider.GetRequiredService<SharedSettings>(), workingDirectory));
        // Without it a standalone run would skip every module that requires the coordinator.
        builder.AddCapabilityProvider<CoordinatorCapabilityProvider>();
        // Every promise the modules create is awaited, so this only reports a forgotten one. Left unset, ProtoPromise rethrows it on
        // the thread pool, which ends the process.
        Promise.Config.UncaughtRejectionHandler ??= static exception => Console.Error.WriteLine($"Unobserved promise rejection: {exception}");
    }
}
