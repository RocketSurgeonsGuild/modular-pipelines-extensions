using Rocket.Surgery.ModularPipelines.Extensions.Joints;

namespace Rocket.Surgery.ModularPipelines.Extensions;

/// <summary>
///     Keeps <see cref="ModulesJoint" /> from adding the module: it runs only when something adds it on purpose, such as
///     <c>builder.AddModule&lt;T&gt;()</c>, or as another module's dependency.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExcludeModuleAttribute : Attribute;
