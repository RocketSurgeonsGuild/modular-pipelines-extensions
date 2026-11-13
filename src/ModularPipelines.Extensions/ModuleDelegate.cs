using Rocket.Surgery.ModularPipelines.Extensions.Joints;

namespace Rocket.Surgery.ModularPipelines.Extensions;

/// <summary>Decides whether <see cref="ModulesJoint" /> adds a module; set it in the Dovetail context to leave modules out.</summary>
/// <param name="moduleType">The module type.</param>
/// <returns><see langword="false" /> to leave the module out.</returns>
public delegate bool ModuleDelegate(Type moduleType);
