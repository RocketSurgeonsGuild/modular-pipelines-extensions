using Joinery.Dovetail;
using Joinery.Dovetail.Attributes;
using Joinery.Dovetail.Joints;
using Microsoft.Extensions.DependencyInjection;

namespace Rocket.Surgery.ModularPipelines.Extensions.Joints;

/// <summary>
///     Registers every class marked with Specular's <see cref="ServiceRegistrationAttribute" /> (the library's settings among them),
///     through the <see cref="ISpecularProvider" /> the build host's generated <c>Imports</c> put in the context.
/// </summary>
[DovetailExport]
public sealed class ServiceRegistrationsJoint : IServiceJoint
{
    /// <inheritdoc />
    /// <exception cref="KeyNotFoundException">No <see cref="ISpecularProvider" /> is in the context: the build host does not reference Specular.</exception>
    public void Register(IDovetailContext context, IServiceCollection services) =>
        services.AddSpecularServiceRegistrations(context.Require<ISpecularProvider>());
}
