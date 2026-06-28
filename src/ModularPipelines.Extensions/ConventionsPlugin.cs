using System.Reflection;
using Indago.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using ServiceCollectionExtensions = ModularPipelines.Extensions.ServiceCollectionExtensions;

namespace Rocket.Surgery.ModularPipelines.Extensions;

public delegate bool ModuleDelegate(Type moduleType);

public class ConventionsPlugin(ConventionContextBuilder contextBuilder, ModuleDelegate? moduleDelegate = default) : IModularPipelinesPlugin
{
    public string Name => "Conventions";

    public void ConfigurePipeline(PipelineBuilder pipelineBuilder) => pipelineBuilder.Configure(contextBuilder, CancellationToken.None).Wait();

    public void ConfigureServices(IServiceCollection services)
    {
        var ctp = Assembly.GetEntryAssembly().GetIndagoProvider();
        services.AddIndagoServiceRegistrations(ctp);
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
            if (moduleDelegate?.Invoke(m) == false)
            {
                continue;
            }
            method.MakeGenericMethod(m)!.Invoke(null, [services]);
        }
    }
}
