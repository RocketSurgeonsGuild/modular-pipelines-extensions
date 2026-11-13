using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using ModularPipelines.Reporting;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Splits the selected test projects between the test shards (see <see cref="TestShards" />), once for the whole run.
/// </summary>
/// <remarks>
///     The split is this module's result, which every shard depends on, so all of them work from the same split. Recomputing it in
///     each shard could run a project twice or not at all if the durations changed in between. Projects are placed longest first by
///     the durations their sub modules took last time (under <see cref="TestSolution" /> or a shard), or round-robin by path when there
///     are none.
/// </remarks>
[DependsOn<RestoreSolution>]
public partial class TestShardPlan
(
    SharedSettings sharedSettings,
    TestSettings testSettings,
    TestShardSet shards,
    IModuleEstimatedTimeProvider estimates,
    SolutionSettings settings = null!) : Module<TestShardPlan.Result?>
{
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(settings is null || !testSettings.IsEnabled || !shards.IsEnabled,
            "Tests are disabled, no solution was provided or there are no test shards")
    );

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        // Cleaned here, before any shard starts, because shards that share this machine must not clean each other's results.
        testSettings.TestsDirectory.Clean();

        var projects = await SolutionProjects.EvaluateAsync(context, sharedSettings, settings.Solution, cancellationToken);
        SolutionProjects.WarnAboutUnmatchedPatterns(context, projects, testSettings);
        var selected = projects.Where(project => project.IsTestProject && SolutionProjects.IsSelected(project, testSettings)).ToArray();

        var durations = await GetDurationsAsync(cancellationToken);
        var shardAssignments = TestShardPartitioner.Partition(
            selected.Select(project => new TestShardItem(
                    project.RootPath,
                    durations.TryGetValue(TestProjectRunner.GetName(project, testSettings), out var duration) ? duration : null
                )
            ),
            shards.Count
        );

        if (context.Logger.IsEnabled(LogLevel.Information))
        {
            context.Logger.LogInformation(
                "Test projects split between {Count} shards:{Shards}",
                shards.Count,
                string.Concat(
                    shardAssignments.Select(shard =>
                        $"{Environment.NewLine}  shard {shard.Number}: {( shard.Projects.Count == 0 ? "nothing" : string.Join(", ", shard.Projects) )} (estimated {shard.Estimate})"
                    )
                )
            );
        }

        return new([.. projects], [.. shardAssignments]);
    }

    // The last duration of each project's sub module, under TestSolution or under any shard; a shard's is the newer when both exist.
    private async Task<Dictionary<string, TimeSpan>> GetDurationsAsync(CancellationToken cancellationToken)
    {
        var durations = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        foreach (var moduleType in shards.ModuleTypes.Prepend(typeof(TestSolution)))
        {
            foreach (var estimate in await estimates.GetSubModuleEstimatedTimesAsync(ModuleId.FromType(moduleType), cancellationToken))
            {
                durations[estimate.SubModuleName] = estimate.EstimatedDuration;
            }
        }

        return durations;
    }

    /// <summary>The split of the test projects between the shards.</summary>
    /// <param name="Projects">Every project of the solution, which the default coverage settings are scoped by.</param>
    /// <param name="Shards">The projects of each shard, in shard order.</param>
    public record Result(ImmutableList<SolutionProject> Projects, ImmutableList<TestShardAssignment> Shards);
}
