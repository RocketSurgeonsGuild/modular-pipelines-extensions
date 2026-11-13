using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Rocket.Surgery.ModularPipelines.Extensions.History;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Splits the selected test projects between the test shards (see <see cref="TestShards" />), once for the whole run.
/// </summary>
/// <remarks>
///     The split is this module's result, which every shard depends on, so all of them work from the same split. Recomputing it in
///     each shard could run a project twice or not at all, since each project's run is recorded as soon as it finishes. Projects are
///     placed longest first by how long each took in its last run (<see cref="TestProjectHistory" />, recorded by
///     <see cref="TestSolution" /> or a shard), or round-robin by path when none of them has run before.
/// </remarks>
[DependsOn<RestoreSolution>]
public partial class TestShardPlan
(
    SharedSettings sharedSettings,
    TestSettings testSettings,
    TestShardSet shards,
    SolutionSettings settings = null!,
    TestProjectHistory? projectHistory = null) : Module<TestShardPlan.Result?>
{
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(settings is null || !testSettings.IsEnabled || !shards.IsEnabled,
            "Tests are disabled, no solution was provided or there are no test shards")
    );

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        // Cleaned here, before any shard starts, because the shards must not clean each other's results.
        testSettings.TestsDirectory.Clean();

        var projects = await SolutionProjects.EvaluateAsync(context, sharedSettings, settings.Solution, cancellationToken);
        SolutionProjects.WarnAboutUnmatchedPatterns(context, projects, testSettings);
        var selected = projects.Where(project => project.IsTestProject && SolutionProjects.IsSelected(project, testSettings)).ToArray();

        var lastRuns = projectHistory is null
            ? []
            : await projectHistory.GetLastRunsAsync(cancellationToken);
        var items = GetItems(selected.Select(project => (project.RootPath, TestProjectRunner.GetName(project, testSettings))), lastRuns);
        var shardAssignments = TestShardPartitioner.Partition(items, shards.Count);

        if (context.Logger.IsEnabled(LogLevel.Information))
        {
            context.Logger.LogInformation("{Plan}", Describe(shardAssignments, items, lastRuns));
        }

        return new([.. projects], [.. shardAssignments]);
    }

    /// <summary>
    ///     The projects to split, each with the duration of its last run, failed or not, keyed by its sub module name;
    ///     <see langword="null" /> for a project that has not run before.
    /// </summary>
    internal static ImmutableArray<TestShardItem> GetItems(
        IEnumerable<(string Key, string Name)> projects,
        IReadOnlyDictionary<string, TestProjectRun> lastRuns
    ) =>
    [
        .. projects.Select(project => new TestShardItem(
                project.Key,
                lastRuns.TryGetValue(project.Name, out var run) ? run.Duration : null,
                project.Name
            )
        ),
    ];

    /// <summary>The log line that says how the projects were split, and from what.</summary>
    internal static string Describe(
        IReadOnlyList<TestShardAssignment> assignments,
        IReadOnlyList<TestShardItem> items,
        IReadOnlyDictionary<string, TestProjectRun> lastRuns
    )
    {
        var byKey = items.DistinctBy(item => item.Key, StringComparer.Ordinal).ToDictionary(item => item.Key, StringComparer.Ordinal);
        var fallback = TestShardPartitioner.GetFallback(items);
        var source = fallback is null
            ? "round-robin by path: no test project has a previous run"
            : "by the duration of each project's last run";

        return $"Test projects split between {assignments.Count} shards {source}:"
          + string.Concat(
                assignments.Select(shard =>
                    $"{Environment.NewLine}  shard {shard.Number}"
                  + ( fallback is null ? "" : $" (estimated {Format(shard.Estimate)})" )
                  + ": "
                  + ( shard.Projects.Count == 0
                        ? "nothing"
                        : string.Join(", ", shard.Projects.Select(key => $"{key} ({DescribeProject(byKey[key], lastRuns, fallback)})")) )
                )
            );
    }

    private static string DescribeProject(TestShardItem item, IReadOnlyDictionary<string, TestProjectRun> lastRuns, TimeSpan? fallback) =>
        item.Name is { } name && lastRuns.TryGetValue(name, out var run)
            ? $"last run {Format(run.Duration)}{( run.Succeeded ? "" : ", failed" )}"
            : fallback is { } median
                ? $"no previous run, counted as the median {Format(median)}"
                : "no previous run";

    private static string Format(TimeSpan duration) => duration.TotalSeconds.ToString("0.0s", CultureInfo.InvariantCulture);

    /// <summary>The split of the test projects between the shards.</summary>
    /// <param name="Projects">Every project of the solution, which the default coverage settings are scoped by.</param>
    /// <param name="Shards">The projects of each shard, in shard order.</param>
    public record Result(ImmutableList<SolutionProject> Projects, ImmutableList<TestShardAssignment> Shards);
}
