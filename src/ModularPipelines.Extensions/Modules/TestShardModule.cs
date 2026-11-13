using Microsoft.Extensions.Logging;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>The services a test shard uses, in one parameter so the generated shards stay one line each.</summary>
[PublicAPI]
public sealed class TestShardServices(ArtifactSettings artifactSettings, TestSettings testSettings, SolutionSettings? solutionSettings = null)
{
    internal ArtifactSettings ArtifactSettings => artifactSettings;
    internal TestSettings TestSettings => testSettings;
    internal SolutionSettings? SolutionSettings => solutionSettings;
}

/// <summary>
///     The base of the generated <c>TestShard1</c> to <c>TestShardN</c> modules: runs the test projects <see cref="TestShardPlan" />
///     gave this shard, exactly as <see cref="TestSolution" /> runs each project in <see cref="TestMode.PerProject" /> mode.
/// </summary>
/// <param name="services">The shard's services.</param>
/// <param name="number">The shard's number, from one.</param>
[DependsOn<BuildSolution>]
[DependsOn<TestShardPlan>]
[PublicAPI]
public abstract class TestShardModule(TestShardServices services, int number) : Module<TestSolution.Result?>
{
    /// <summary>The shard's number, from one.</summary>
    public int Number => number;

    /// <inheritdoc />
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(services.SolutionSettings is null || !services.TestSettings.IsEnabled,
            "Tests are disabled or no solution was provided")
    );

    /// <inheritdoc />
    protected sealed override async Task<TestSolution.Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var testSettings = services.TestSettings;
        var settings = services.SolutionSettings!;

        var plan = ( await context.GetModule<TestShardPlan>() ).ValueOrDefault
         ?? throw new InvalidOperationException($"{nameof(TestShardPlan)} has no result, so test shard {number} does not know its projects");
        var assignment = plan.Shards.Single(shard => shard.Number == number);
        var mine = assignment.Projects.ToHashSet(StringComparer.Ordinal);
        var projects = plan.Projects.Where(project => mine.Contains(project.RootPath)).ToArray();
        var binlog = services.ArtifactSettings.LogsDirectory.GetFile($"tests.shard-{number}.binlog");
        if (projects.Length == 0)
        {
            if (context.Logger.IsEnabled(LogLevel.Information)) context.Logger.LogInformation("Test shard {Number} has no test projects to run", number);
            return new(null, [], binlog, []);
        }

        foreach (var project in projects)
        {
            var resultsDirectory = TestProjectRunner.GetResultsDirectory(project, testSettings);
            if (resultsDirectory.Exists) resultsDirectory.Delete();
        }

        // A file of its own: the shards write the default settings side by side.
        var runSettings = testSettings.RunSettings;
        if (testSettings.UsesDefaultRunSettings || !runSettings.Exists)
        {
            runSettings = testSettings.TestsDirectory.GetFile($"coverage.shard-{number}.runsettings");
            await SolutionProjects.WriteDefaultRunSettingsAsync(runSettings, plan.Projects, testSettings, cancellationToken);
        }

        var outcomes = await TestProjectRunner.RunProjectsAsync(context, projects, settings, testSettings, runSettings, cancellationToken);

        return TestProjectRunner.ThrowIfAnyFailed(
            new(null, [.. outcomes.SelectMany(outcome => TestProjectRunner.FindTrxFiles(outcome.ResultsDirectory))], binlog, outcomes)
        );
    }
}
