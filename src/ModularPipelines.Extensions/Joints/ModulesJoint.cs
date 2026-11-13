using Joinery.Dovetail;
using Joinery.Dovetail.Attributes;
using Joinery.Dovetail.Joints;
using Specular.Abstractions;

namespace Rocket.Surgery.ModularPipelines.Extensions.Joints;

/// <summary>
///     Adds every module class of the build host and of the assemblies it references (the library's modules among them), as Specular
///     found them when the build host was compiled.
/// </summary>
/// <remarks>
///     <para>
///         A <see cref="ModuleDelegate" /> in the context (<c>ConfigureDovetail(z =&gt; z.Set&lt;ModuleDelegate&gt;(...))</c>) decides
///         which of them are added; leave this joint out (<c>ExceptJoint(typeof(ModulesJoint))</c>) to add every module yourself.
///     </para>
/// </remarks>
[DovetailExport]
[RequiresUnreferencedCode("Adds the modules Specular found by their type, which ModularPipelines constructs through reflection.")]
[RequiresDynamicCode("Adds the modules Specular found by their type, which ModularPipelines constructs through reflection.")]
public sealed class ModulesJoint : IPipelineBuilderJoint
{
    /// <inheritdoc />
    /// <exception cref="KeyNotFoundException">No <see cref="ISpecularProvider" /> is in the context: the build host does not reference Specular.</exception>
    public void Register(IDovetailContext context, PipelineBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(builder);
        var filter = context.Get<ModuleDelegate>();
        var modules = context
                     .Require<ISpecularProvider>()
                     .GetTypes(s => s
                                   .FromAssemblies()
                                   .GetTypes(f => f
                                                 .AssignableTo<IModule>()
                                                 .NotInfoOf(TypeInfoFilter.Static, TypeInfoFilter.Abstract)
                                                 .KindOf(TypeKindFilter.Class)
                                    )
                      );
        builder.AddModules([.. modules.Where(module => filter?.Invoke(module) != false)]);
    }
}
