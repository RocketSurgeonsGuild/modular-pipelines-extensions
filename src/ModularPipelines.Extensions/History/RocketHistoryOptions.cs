using Microsoft.Extensions.Configuration;

namespace Rocket.Surgery.ModularPipelines.Extensions.History;

/// <summary>
///     How <see cref="RocketHistoryExtensions.AddRocketHistory" /> keeps run history and estimates module durations. Read from the
///     <c>History</c> configuration section by <see cref="FromConfiguration" />.
/// </summary>
public sealed record RocketHistoryOptions
{
    /// <summary>The default history folder, relative to <see cref="RootDirectory" />.</summary>
    public static readonly string DefaultHistoryDirectory = Path.Combine(".modularpipelines", "run-history");

    /// <summary>The default estimates folder, relative to <see cref="RootDirectory" />.</summary>
    public static readonly string DefaultEstimatesDirectory = Path.Combine(".modularpipelines", "estimates");

    /// <summary>The default module cache folder, relative to <see cref="RootDirectory" />.</summary>
    public static readonly string DefaultModuleCacheDirectory = Path.Combine(".modularpipelines", "module-cache");

    /// <summary>
    ///     Whether run history and the history-based estimates are on (<c>History:Enabled</c>, default <see langword="true" />). Off,
    ///     ModularPipelines keeps no run history at all.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The repository root that relative folders resolve against.</summary>
    public required string RootDirectory { get; init; }

    /// <summary>
    ///     Where the run reports are kept (<c>History:Directory</c>). Relative to <see cref="RootDirectory" /> unless fully qualified,
    ///     so a CI cache can restore it.
    /// </summary>
    public string HistoryDirectory { get; init; } = DefaultHistoryDirectory;

    /// <summary>
    ///     Where the last durations of modules and their sub modules are kept (<c>History:EstimatesDirectory</c>). Run history only
    ///     records whole modules, so sub module estimates live here, as does the last run of each test project
    ///     (<see cref="TestProjectHistory" />).
    /// </summary>
    public string EstimatesDirectory { get; init; } = DefaultEstimatesDirectory;

    /// <summary>How many runs are kept for this pipeline identity (<c>History:Retention</c>, default 30).</summary>
    public int Retention { get; init; } = 30;

    /// <summary>
    ///     The pipeline identity run history is grouped by (<c>History:Identity</c>). Defaults to <c>GITHUB_REPOSITORY</c> and
    ///     <c>GITHUB_WORKFLOW</c> on GitHub Actions, and to the name of the root directory elsewhere, so it survives adding or removing
    ///     modules (ModularPipelines would otherwise hash the module list).
    /// </summary>
    public string? PipelineIdentity { get; init; }

    /// <summary>How many past successful runs a module's estimate is the median of (<c>History:Samples</c>, default 10).</summary>
    public int EstimateSamples { get; init; } = 10;

    /// <summary>
    ///     Whether the file-system module cache is registered (<c>History:ModuleCache:Enabled</c>, default <see langword="false" />). It
    ///     only skips modules that declare their inputs with <c>[CacheInputs]</c>.
    /// </summary>
    public bool ModuleCache { get; init; }

    /// <summary>Where the module cache is kept (<c>History:ModuleCache:Directory</c>).</summary>
    public string ModuleCacheDirectory { get; init; } = DefaultModuleCacheDirectory;

    internal string Resolve(string path) => Path.GetFullPath(path, RootDirectory);

    /// <summary>Reads the options from the <c>History</c> section and the environment.</summary>
    /// <param name="configuration">The pipeline configuration.</param>
    /// <param name="workingDirectory">The pipeline's working directory, used to find the repository root.</param>
    public static RocketHistoryOptions FromConfiguration(IConfiguration configuration, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("History");
        var root = configuration.GetValue<string?>("RootDirectory") is { Length: > 0 } configuredRoot
            ? Path.GetFullPath(configuredRoot, workingDirectory)
            : PipelineDirectory.FindGitRoot(Path.Combine(workingDirectory, "_"));

        var defaults = new RocketHistoryOptions { RootDirectory = root };
        return defaults with
        {
            Enabled = section.GetValue("Enabled", defaults.Enabled),
            HistoryDirectory = section.GetValue<string?>("Directory") ?? defaults.HistoryDirectory,
            EstimatesDirectory = section.GetValue<string?>("EstimatesDirectory") ?? defaults.EstimatesDirectory,
            Retention = section.GetValue("Retention", defaults.Retention),
            EstimateSamples = section.GetValue("Samples", defaults.EstimateSamples),
            PipelineIdentity = section.GetValue<string?>("Identity") ?? DefaultIdentity(root),
            ModuleCache = section.GetValue("ModuleCache:Enabled", defaults.ModuleCache),
            ModuleCacheDirectory = section.GetValue<string?>("ModuleCache:Directory") ?? defaults.ModuleCacheDirectory,
        };
    }

    private static string DefaultIdentity(string root)
    {
        var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (string.IsNullOrWhiteSpace(repository)) return Path.GetFileName(Path.TrimEndingDirectorySeparator(root));

        var workflow = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW");
        return string.IsNullOrWhiteSpace(workflow) ? repository : $"{repository}/{workflow}";
    }
}
