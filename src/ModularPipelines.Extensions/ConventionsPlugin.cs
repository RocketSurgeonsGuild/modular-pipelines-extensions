using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.DependencyInjection.Compiled;
using ServiceCollectionExtensions = ModularPipelines.Extensions.ServiceCollectionExtensions;

namespace build.library;

public class ConventionsPlugin(ConventionContextBuilder contextBuilder) : IModularPipelinesPlugin
{
    public string Name => "Conventions";

    public void ConfigurePipeline(PipelineBuilder pipelineBuilder) => pipelineBuilder.Configure(contextBuilder, CancellationToken.None).Wait();

    public void ConfigureServices(IServiceCollection services)
    {
        var ctp = Assembly.GetEntryAssembly().GetCompiledTypeProvider();
        services.AddCompiledServiceRegistrations(ctp);
        // not sure if this is going to come back to haunt me or not
        var modules = ctp.GetTypes(f => f
                                       .FromAssemblies()
                                       .GetTypes(c => c
                                                     .AssignableTo<IModule>()
                                                     .NotInfoOf(TypeInfoFilter.Static, TypeInfoFilter.Abstract)
                                                     .KindOf(TypeKindFilter.Class)
                                        )
        );
        var method = typeof(ServiceCollectionExtensions)
                    .GetMethods()
                    .Single(z => z.Name == nameof(ServiceCollectionExtensions.AddModule) && z.ContainsGenericParameters && z.GetParameters().Length == 1);

        foreach (var m in modules)
        {
            method.MakeGenericMethod(m)!.Invoke(null, [services]);
        }
    }
}
