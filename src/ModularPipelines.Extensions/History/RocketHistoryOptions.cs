using Joinery.Dovetail.Attributes;

namespace Rocket.Surgery.ModularPipelines.Extensions.History;

/// <summary>
///     How <see cref="Joints.RunHistoryJoint" /> keeps run history, duration estimates and the module cache under the repository: the
///     <c>History</c> configuration section. Folders are relative to the repository root unless fully qualified.
/// </summary>
[DovetailConfiguration("History")]
public sealed class RocketHistoryOptions
{
    /// <summary>Whether run history is kept. Off, the estimates are the last duration of each module.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the run reports are kept.</summary>
    public string Directory { get; set; } = ".modularpipelines/run-history";

    /// <summary>
    ///     Where the durations run history does not keep are kept: each sub module's last runs, and each module's last run for a module
    ///     with no history yet.
    /// </summary>
    public string EstimatesDirectory { get; set; } = ".modularpipelines/estimates";

    /// <summary>How many runs are kept for this pipeline identity.</summary>
    public int Retention { get; set; } = 50;

    /// <summary>How many past runs an estimate is the median of.</summary>
    public int Samples { get; set; } = 10;

    /// <summary>
    ///     The pipeline identity run history is grouped by. Defaults to <c>GITHUB_REPOSITORY/GITHUB_WORKFLOW</c> on GitHub Actions, and to
    ///     the name of the root folder elsewhere, so it survives adding or removing modules (ModularPipelines would hash the module list).
    /// </summary>
    public string? Identity { get; set; }

    /// <summary>The module cache.</summary>
    public RocketModuleCacheOptions ModuleCache { get; set; } = new();
}

/// <summary>The module cache settings of <see cref="RocketHistoryOptions" />.</summary>
public sealed class RocketModuleCacheOptions
{
    /// <summary>
    ///     Whether the file-system module cache is registered. It only restores modules that declare what their result depends on
    ///     (<c>[CacheInputs]</c>, <c>WithCacheKeyPart</c>, <c>WithCacheEnvironmentVariable</c> or <c>WithCacheAssemblyVersionKey</c>);
    ///     <c>--no-cache</c> bypasses it for one run.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the module cache is kept.</summary>
    public string Directory { get; set; } = ".modularpipelines/module-cache";
}
