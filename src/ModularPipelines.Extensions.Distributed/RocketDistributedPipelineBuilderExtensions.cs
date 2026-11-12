using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rocket.Surgery.ModularPipelines.Extensions.Distributed;

/// <summary>
///     Turns on ModularPipelines distributed mode, coordinated through Redis, with defaults suited to GitHub Actions.
/// </summary>
[PublicAPI]
public static class RocketDistributedPipelineBuilderExtensions
{
    /// <summary>
    ///     Runs the pipeline across several processes when a Redis connection is configured and the run has more than one instance;
    ///     otherwise registers nothing, so the run is the normal standalone run. Settings are read with
    ///     <see cref="RocketDistributedOptions.FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration)" /> and then passed to
    ///     <paramref name="configure" />.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="configure">Changes the settings read from the environment and configuration.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="InvalidOperationException">Distributed mode was already added to this builder.</exception>
    [RequiresUnreferencedCode("Distributed type-erased result serialization is unsupported in trimmed applications.")]
    [RequiresDynamicCode("Distributed type-erased result serialization is unsupported in Native AOT.")]
    public static PipelineBuilder AddRocketDistributed(this PipelineBuilder builder, Action<RocketDistributedOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Services.Any(static d => d.ServiceType == typeof(RocketDistributedRegistration)))
        {
            throw new InvalidOperationException(
                "AddRocketDistributed was already applied to this pipeline, possibly by the convention the conventions plugin runs. "
              + "Call it before adding the conventions plugin, so the convention sees it and steps aside."
            );
        }

        var options = RocketDistributedOptions.FromConfiguration(builder.Configuration);
        configure?.Invoke(options);
        return builder.AddRocketDistributed(options);
    }

    [RequiresUnreferencedCode("Distributed type-erased result serialization is unsupported in trimmed applications.")]
    [RequiresDynamicCode("Distributed type-erased result serialization is unsupported in Native AOT.")]
    internal static PipelineBuilder AddRocketDistributed(this PipelineBuilder builder, RocketDistributedOptions options)
    {
        // Recorded even for a standalone run, so an explicit call and the convention never both apply.
        builder.Services.TryAddSingleton(new RocketDistributedRegistration(options.IsDistributed));
        if (!options.IsDistributed) return builder;

        // Core advertises the coordinator capability on every process that is not a worker; adding it here as well keeps the
        // master's capability independent of how core detects the role.
        Capability[] capabilities = options.IsCoordinator
            ? [new(RocketCapabilities.Coordinator), .. options.Capabilities]
            : [.. options.Capabilities];

        builder.AddDistributedMode(d =>
            {
                var result = d with
                {
                    Role = options.Role,
                    InstanceIndex = options.InstanceIndex,
                    TotalInstances = options.TotalInstances,
                    RunId = options.RunId ?? d.RunId,
                    MaxParallelism = options.MaxParallelism ?? d.MaxParallelism,
                    Capabilities = [.. d.Capabilities, .. capabilities],
                    // The master also runs modules, so dispatch starts at once and workers join when their runners are up.
                    MinimumWorkerCount = 0,
                    WorkerRegistrationTimeout = options.WorkerRegistrationTimeout,
                    WorkerHeartbeatInterval = options.WorkerHeartbeatInterval,
                    WorkerTimeout = options.WorkerTimeout,
                    MasterTimeout = options.MasterTimeout,
                    ModuleResultTimeout = options.ModuleResultTimeout,
                    RunCleanup = options.RunCleanup,
                };
                return options.ConfigureDistributed?.Invoke(result) ?? result;
            }
        );

        RedisOptions redis(RedisOptions r)
        {
            var result = r with
            {
                ConnectionString = options.RedisConnectionString!,
                KeyPrefix = options.KeyPrefix,
                TimeToLive = options.TimeToLive,
                MasterStartTimeout = options.MasterStartTimeout,
            };
            return options.ConfigureRedis?.Invoke(result) ?? result;
        }

        return options.UseRedisArtifactStore
            ? builder.AddRedisDistributed(redis)
            : builder.AddRedisDistributedCoordinator(redis);
    }
}

/// <summary>
///     Marks a pipeline <see cref="RocketDistributedPipelineBuilderExtensions.AddRocketDistributed(PipelineBuilder, Action{RocketDistributedOptions}?)" />
///     was applied to.
/// </summary>
/// <param name="IsDistributed">Whether the run is distributed.</param>
internal sealed record RocketDistributedRegistration(bool IsDistributed);
