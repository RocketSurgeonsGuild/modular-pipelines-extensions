using Microsoft.Extensions.Options;
using ModularPipelines.Distributed;

namespace Rocket.Surgery.ModularPipelines.Extensions;

/// <summary>
///     Advertises <see cref="RocketCapabilities.Coordinator" /> in every process that is not a distributed worker: a standalone run, and
///     the master of a distributed one.
/// </summary>
/// <remarks>
///     ModularPipelines skips a module whose required capability the process lacks when it runs locally, so without this a standalone
///     run would skip every coordinator module. The role is worked out the way ModularPipelines' own <c>RoleDetector</c> and
///     <c>DistributedConditionRouting.IsWorker</c> do: a run is distributed only with more than one instance, and then an instance is a
///     worker when its role says so, or, with the automatic role, when its index is not zero.
/// </remarks>
public sealed class CoordinatorCapabilityProvider(IOptions<DistributedOptions> options) : ICapabilityProvider
{
    /// <summary>Whether this process is a distributed worker.</summary>
    public static bool IsWorker(DistributedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.TotalInstances > 1
            && options.Role switch
            {
                DistributedRole.Worker => true,
                DistributedRole.Master => false,
                DistributedRole.Auto => options.InstanceIndex != 0,
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.Role, "Unknown distributed role."),
            };
    }

    /// <inheritdoc />
    public Task<IEnumerable<Capability>> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<Capability>>(IsWorker(options.Value) ? [] : [new Capability(RocketCapabilities.Coordinator)]);
}
