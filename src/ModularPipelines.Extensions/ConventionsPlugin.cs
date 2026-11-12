using System.Reflection;
using Indago.Abstractions;

namespace Rocket.Surgery.ModularPipelines.Extensions;

public delegate bool ModuleDelegate(Type moduleType);

public class ConventionsPlugin(ConventionContextBuilder contextBuilder, ModuleDelegate? moduleDelegate = default) : IModularPipelinesPlugin
{
    public string Name => "Conventions";

    [RequiresUnreferencedCode("Registers the modules found by the scan by their runtime type.")]
    [RequiresDynamicCode("Registers the modules found by the scan by their runtime type.")]
    public void Configure(PipelineBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Configure(contextBuilder, CancellationToken.None).Wait();

        var ctp = Assembly.GetEntryAssembly().GetIndagoProvider();
        builder.Services.AddIndagoServiceRegistrations(ctp);
        // not sure if this is going to come back to haunt me or not
        var modules = ctp.GetTypes(f => f
                                       .FromAssemblies()
                                       .GetTypes(c => c
                                                     .AssignableTo<IModule>()
                                                     .NotInfoOf(TypeInfoFilter.Static, TypeInfoFilter.Abstract)
                                                     .KindOf(TypeKindFilter.Class)
                                        )
        );

        builder.AddModules([.. modules.Where(m => moduleDelegate?.Invoke(m) != false)]);
    }
}
