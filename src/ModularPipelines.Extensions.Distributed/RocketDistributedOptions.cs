using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Rocket.Surgery.ModularPipelines.Extensions.Distributed;

/// <summary>
///     Settings for <see cref="RocketDistributedPipelineBuilderExtensions.AddRocketDistributed" />. The defaults come from the standard
///     <c>MODULARPIPELINES_*</c> and GitHub Actions environment variables and the <c>Pipeline:Distributed</c> configuration section.
/// </summary>
[PublicAPI]
public sealed class RocketDistributedOptions
{
    /// <summary>
    ///     The configuration section the options are read from.
    /// </summary>
    public const string ConfigurationSection = "Pipeline:Distributed";

    /// <summary>
    ///     The environment variable that holds the Redis connection string. Distributed mode stays off while it is empty.
    /// </summary>
    public const string RedisEnvironmentVariable = "REDIS_URL";

    /// <summary>
    ///     The environment variable that holds this process's zero-based instance index.
    /// </summary>
    public const string InstanceIndexEnvironmentVariable = "MODULARPIPELINES_INSTANCE_INDEX";

    /// <summary>
    ///     The environment variable that holds the number of processes taking part in the run.
    /// </summary>
    public const string TotalInstancesEnvironmentVariable = "MODULARPIPELINES_TOTAL_INSTANCES";

    /// <summary>
    ///     The environment variable that holds the identifier every process of the run shares.
    /// </summary>
    public const string RunIdEnvironmentVariable = "MODULARPIPELINES_RUN_ID";

    /// <summary>
    ///     The environment variable that forces the role (<c>Auto</c>, <c>Master</c> or <c>Worker</c>).
    /// </summary>
    public const string RoleEnvironmentVariable = "MODULARPIPELINES_ROLE";

    /// <summary>
    ///     The environment variable that limits how many modules run at once on each process.
    /// </summary>
    public const string MaxParallelismEnvironmentVariable = "MODULARPIPELINES_MAX_PARALLELISM";

    /// <summary>
    ///     Whether distributed mode may be turned on at all (<c>Pipeline:Distributed:Enabled</c>). Defaults to <see langword="true" />;
    ///     set it to <see langword="false" /> to force a standalone run even when Redis is configured.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     The Redis connection string, from <c>REDIS_URL</c> or <c>Pipeline:Distributed:Redis</c>.
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    ///     This process's role. <see cref="DistributedRole.Auto" /> makes instance zero the master.
    /// </summary>
    public DistributedRole Role { get; set; } = DistributedRole.Auto;

    /// <summary>
    ///     This process's zero-based index in the run.
    /// </summary>
    public int InstanceIndex { get; set; }

    /// <summary>
    ///     The number of processes in the run, master included. Distributed mode needs at least two.
    /// </summary>
    public int TotalInstances { get; set; } = 1;

    /// <summary>
    ///     The identifier every process of the run shares. Defaults to <c>MODULARPIPELINES_RUN_ID</c>, or to
    ///     <c>{GITHUB_RUN_ID}-{GITHUB_RUN_ATTEMPT}</c> on GitHub Actions.
    /// </summary>
    public string? RunId { get; set; }

    /// <summary>
    ///     An optional limit on modules running at once on each process.
    /// </summary>
    public int? MaxParallelism { get; set; }

    /// <summary>
    ///     The prefix of every Redis key, so several repositories can share one Redis. Defaults to <c>rocket-{owner}-{repository}</c> on
    ///     GitHub Actions and <c>rocket</c> elsewhere.
    /// </summary>
    public string KeyPrefix { get; set; } = "rocket";

    /// <summary>
    ///     How long the master waits for a worker that can run a module it cannot run itself. Runners of a matrix start minutes apart,
    ///     so this is longer than the ModularPipelines default.
    /// </summary>
    public TimeSpan WorkerRegistrationTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     How long a worker waits for the master to start before it gives up.
    /// </summary>
    public TimeSpan MasterStartTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     How long a worker keeps going without a sign of life from a started master.
    /// </summary>
    public TimeSpan MasterTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     How often workers report liveness and renew their leases.
    /// </summary>
    public TimeSpan WorkerHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How long a worker's registration and leases last without a heartbeat. Hosted runners stall now and then under load, so this
    ///     is longer than the ModularPipelines default.
    /// </summary>
    public TimeSpan WorkerTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     The master's backstop for a claimed module's result.
    /// </summary>
    public TimeSpan ModuleResultTimeout { get; set; } = TimeSpan.FromMinutes(45);

    /// <summary>
    ///     How long the run's Redis keys live without renewal. Must exceed <see cref="ModuleResultTimeout" />.
    /// </summary>
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    ///     When the master deletes the run's Redis data. Defaults to keeping a failed run's data until it expires.
    /// </summary>
    public DistributedRunCleanupPolicy RunCleanup { get; set; } = DistributedRunCleanupPolicy.OnSuccess;

    /// <summary>
    ///     Whether artifacts travel through Redis. Set it to <see langword="false" /> and register another
    ///     <see cref="IDistributedArtifactStore" /> (for example the S3 store of <c>ModularPipelines.Distributed.Artifacts.S3</c>) for large
    ///     artifacts.
    /// </summary>
    public bool UseRedisArtifactStore { get; set; } = true;

    /// <summary>
    ///     Extra capabilities this process advertises. The master always advertises <see cref="RocketCapabilities.Coordinator" />.
    /// </summary>
    public IList<Capability> Capabilities { get; } = [];

    /// <summary>
    ///     A last transformation of the ModularPipelines distributed options.
    /// </summary>
    public Func<DistributedOptions, DistributedOptions>? ConfigureDistributed { get; set; }

    /// <summary>
    ///     A last transformation of the Redis options, for example to set TLS or timeouts on the connection.
    /// </summary>
    public Func<RedisOptions, RedisOptions>? ConfigureRedis { get; set; }

    /// <summary>
    ///     Whether these settings turn distributed mode on: it is enabled, a Redis connection is configured and the run has more than
    ///     one instance.
    /// </summary>
    public bool IsDistributed => Enabled && !string.IsNullOrWhiteSpace(RedisConnectionString) && TotalInstances > 1;

    /// <summary>
    ///     Whether this process is the master, which coordinates the run.
    /// </summary>
    public bool IsCoordinator => Role == DistributedRole.Master || ( Role == DistributedRole.Auto && InstanceIndex == 0 );

    /// <summary>
    ///     Reads the options from the environment and from the <c>Pipeline:Distributed</c> section of <paramref name="configuration" />.
    ///     Environment variables win over configuration.
    /// </summary>
    /// <param name="configuration">The pipeline configuration.</param>
    /// <returns>The options.</returns>
    public static RocketDistributedOptions FromConfiguration(IConfiguration configuration) =>
        FromConfiguration(configuration, Environment.GetEnvironmentVariable);

    internal static RocketDistributedOptions FromConfiguration(IConfiguration configuration, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var section = configuration.GetSection(ConfigurationSection);

        string? read(string variable, string key) => NullIfEmpty(environment(variable)) ?? NullIfEmpty(section[key]);

        var options = new RocketDistributedOptions
        {
            Enabled = ParseBoolean(section["Enabled"], $"{ConfigurationSection}:Enabled") ?? true,
            RedisConnectionString = read(RedisEnvironmentVariable, "Redis"),
            Role = ParseRole(read(RoleEnvironmentVariable, nameof(Role))),
            InstanceIndex = ParseInt32(read(InstanceIndexEnvironmentVariable, nameof(InstanceIndex)), InstanceIndexEnvironmentVariable, 0) ?? 0,
            TotalInstances = ParseInt32(read(TotalInstancesEnvironmentVariable, nameof(TotalInstances)), TotalInstancesEnvironmentVariable, 1) ?? 1,
            MaxParallelism = ParseInt32(read(MaxParallelismEnvironmentVariable, nameof(MaxParallelism)), MaxParallelismEnvironmentVariable, 1),
            RunId = read(RunIdEnvironmentVariable, nameof(RunId)) ?? GitHubRunId(environment),
            KeyPrefix = NullIfEmpty(section[nameof(KeyPrefix)]) ?? GitHubKeyPrefix(environment) ?? "rocket",
        };
        return options;
    }

    private static string? GitHubRunId(Func<string, string?> environment) =>
        NullIfEmpty(environment("GITHUB_RUN_ID")) is { } runId
            ? $"{runId}-{NullIfEmpty(environment("GITHUB_RUN_ATTEMPT")) ?? "1"}"
            : null;

    private static string? GitHubKeyPrefix(Func<string, string?> environment) =>
        NullIfEmpty(environment("GITHUB_REPOSITORY")) is { } repository
            ? $"rocket-{new string([.. repository.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-')])}"
            : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseInt32(string? value, string name, int minimum) =>
        value is null ? null
        : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= minimum ? parsed
        : throw new InvalidOperationException($"{name} must be an integer greater than or equal to {minimum}, but was '{value}'.");

    private static bool? ParseBoolean(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null
        : bool.TryParse(value, out var parsed) ? parsed
        : throw new InvalidOperationException($"{name} must be true or false, but was '{value}'.");

    private static DistributedRole ParseRole(string? value) =>
        value is null ? DistributedRole.Auto
        : Enum.TryParse<DistributedRole>(value, ignoreCase: true, out var role) && Enum.IsDefined(role) ? role
        : throw new InvalidOperationException($"{RoleEnvironmentVariable} must be Auto, Master or Worker, but was '{value}'.");
}
