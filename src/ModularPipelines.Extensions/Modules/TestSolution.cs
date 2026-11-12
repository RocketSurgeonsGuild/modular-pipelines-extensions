using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModularPipelines.Caching;
using ModularPipelines.Distributed;
using ModularPipelines.Exceptions;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Runs the solution's tests with coverage, either one <c>dotnet test</c> per test project as sub modules (several at once), or
///     one over the solution, as <see cref="TestSettings.Mode" /> says.
/// </summary>
/// <remarks>
///     Sub modules run in this module's process, so the module as a whole is what a distributed run hands to a worker. A worker does
///     not run <see cref="RestoreSolution" /> or <see cref="BuildSolution" /> (they need the coordinator), so there the tests restore
///     and build what they run themselves, and the results are handed back to the coordinator as the <see cref="ResultsArtifact" />
///     artifact, which <see cref="GenerateCodeCoverageReports" /> restores. With test shards (see <see cref="TestShards" />) this module
///     skips itself: the shards run the tests.
/// </remarks>
[DependsOn<RestoreSolution>]
[DependsOn<BuildSolution>]
[ProducesArtifact(ResultsArtifact, ResultsArtifactPath)]
public partial class TestSolution
(
    SharedSettings sharedSettings,
    ArtifactSettings artifactSettings,
    TestSettings testSettings,
    IOptions<DistributedOptions> distributedOptions,
    IOptions<ModuleCacheOptions> moduleCacheOptions,
    SolutionSettings settings = null!,
    TestShardSet? shards = null) : Module<TestSolution.Result?>
{
    /// <summary>The artifact that carries a worker's test results to the coordinator.</summary>
    public const string ResultsArtifact = "rocket-test-results";

    /// <summary>
    ///     Where the results a worker hands back are staged, relative to the pipeline's working directory (the root of artifact paths).
    ///     It always exists, so the artifact is never missing; outside a worker it stays empty.
    /// </summary>
    public const string ResultsArtifactPath = ".modularpipelines/test-results";

    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => settings is null || !testSettings.IsEnabled
            ? SkipDecision.Skip("Tests are disabled or no solution was provided")
            // The shards run every test project between them, so running them here as well would run each test twice.
            : SkipDecision.When(shards is { IsEnabled: true }, $"The tests run in {shards?.Count} test shards")
    );

    internal static FolderPath GetResultsArtifactFolder(ModuleCacheOptions options) =>
        new(Path.GetFullPath(ResultsArtifactPath, options.WorkingDirectory));

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var isWorker = CoordinatorCapabilityProvider.IsWorker(distributedOptions.Value);

        // Cleaned first: the default settings are written into this folder on every run (not only when missing) so a newer library's
        // defaults replace an older copy.
        testSettings.TestsDirectory.Clean();
        var staging = GetResultsArtifactFolder(moduleCacheOptions.Value);
        if (staging.Exists) staging.Delete();
        staging.Create();

        if (isWorker)
        {
            await TestProjectRunner.RestoreOnceAsync(context, settings.Solution, cancellationToken);
        }

        var projects = await SolutionProjects.EvaluateAsync(context, sharedSettings, settings.Solution, cancellationToken);
        SolutionProjects.WarnAboutUnmatchedPatterns(context, projects, testSettings);

        if (testSettings.UsesDefaultRunSettings || !testSettings.RunSettings.Exists)
        {
            await SolutionProjects.WriteDefaultRunSettingsAsync(testSettings.RunSettings, projects, testSettings, cancellationToken);
        }

        var binlog = artifactSettings.LogsDirectory.GetFile("tests.binlog");
        var result = testSettings.Mode == TestMode.Solution
            ? await RunSolutionAsync(context, projects, isWorker, binlog, cancellationToken)
            : await RunProjectsAsync(context, projects, isWorker, binlog, cancellationToken);

        if (isWorker)
        {
            await testSettings.TestsDirectory.CopyToAsync(staging, cancellationToken: cancellationToken);
        }

        return TestProjectRunner.ThrowIfAnyFailed(result with { StagedOnWorker = isWorker });
    }

    private async Task<Result> RunProjectsAsync(
        IModuleContext context,
        IReadOnlyList<SolutionProject> projects,
        bool isWorker,
        FilePath binlog,
        CancellationToken cancellationToken
    )
    {
        var selected = projects.Where(project => project.IsTestProject && SolutionProjects.IsSelected(project, testSettings)).ToArray();
        LogSelection(context, projects, selected);

        var outcomes = await TestProjectRunner.RunProjectsAsync(context, selected, settings, testSettings, testSettings.RunSettings, isWorker, cancellationToken);
        return new(null, TestProjectRunner.FindTrxFiles(testSettings.TestsDirectory), binlog, outcomes);
    }

    private async Task<Result> RunSolutionAsync(
        IModuleContext context,
        IReadOnlyList<SolutionProject> projects,
        bool isWorker,
        FilePath binlog,
        CancellationToken cancellationToken
    )
    {
        var solution = await SolutionToTest(context, projects, cancellationToken);
        var resultsDirectory = testSettings.TestsDirectory;
        try
        {
            var commandResult = await context.Tools.DotNet.TestAsync(
                TestProjectRunner.CreateOptions(settings, testSettings, testSettings.RunSettings, isWorker, resultsDirectory) with { Solution = solution },
                new(),
                cancellationToken
            );
            return new(commandResult, TestProjectRunner.FindTrxFiles(testSettings.TestsDirectory), binlog, [new(settings.Solution.Name, settings.Solution, resultsDirectory, commandResult, null)]);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var commandResult = ( ex as CommandException )?.Result;
            return new(commandResult, TestProjectRunner.FindTrxFiles(testSettings.TestsDirectory), binlog, [new(settings.Solution.Name, settings.Solution, resultsDirectory, commandResult, TestProjectRunner.FirstLine(ex.Message))]);
        }
    }

    private void LogSelection(IModuleContext context, IReadOnlyList<SolutionProject> projects, IReadOnlyList<SolutionProject> selected)
    {
        if (!context.Logger.IsEnabled(LogLevel.Information)) return;

        var left = projects.Where(project => project.IsTestProject).Except(selected).Select(project => project.RootPath).ToArray();
        context.Logger.LogInformation(
            "Running {Count} test projects, {Parallelism} at a time{LeftOut}",
            selected.Count,
            testSettings.Parallelism,
            left.Length == 0 ? "" : $"; leaving out {string.Join(", ", left)}"
        );
    }

    /// <summary>
    ///     The solution, or, when <see cref="TestSettings.IncludeTests" /> or <see cref="TestSettings.ExcludeTests" /> leave test projects
    ///     out, a solution filter of it without them, written to the tests directory. Only the test run uses the filter: the solution
    ///     still builds and packs every project.
    /// </summary>
    private async Task<FilePath> SolutionToTest(IModuleContext context, IReadOnlyList<SolutionProject> projects, CancellationToken cancellationToken)
    {
        if (testSettings.ExcludeTests.Count == 0 && testSettings.IncludeTests.Count == 0) return settings.Solution;

        var included = projects.Where(project => !project.IsTestProject || SolutionProjects.IsSelected(project, testSettings)).ToArray();
        LogSelection(context, projects, [.. included.Where(project => project.IsTestProject)]);

        var filter = testSettings.TestsDirectory.GetFile($"{Path.GetFileNameWithoutExtension(settings.Solution.Name)}.tests.slnf");
        await using (var stream = filter.GetStream(FileAccess.Write))
        await using (var writer = new Utf8JsonWriter(stream, new() { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("solution");
            writer.WriteString("path", Path.GetRelativePath(testSettings.TestsDirectory.Path, settings.Solution.Path).Replace('\\', '/'));
            writer.WriteStartArray("projects");
            foreach (var project in included)
            {
                writer.WriteStringValue(project.SolutionPath);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return filter;
    }

    /// <summary>The outcome of the test run.</summary>
    /// <param name="CommandResult">The result of the single <c>dotnet test</c> in <see cref="TestMode.Solution" /> mode.</param>
    /// <param name="TrxFiles">The TRX report of each test module (one per test project and target framework).</param>
    /// <param name="Binlog">The binary log location.</param>
    /// <param name="Projects">The outcome of each test run: each project, or the solution.</param>
    public record Result(CommandResult? CommandResult, ImmutableList<FilePath> TrxFiles, FilePath Binlog, ImmutableList<ProjectResult> Projects)
    {
        /// <summary>
        ///     Whether a distributed worker produced the result and staged its files as the module's results artifact, which
        ///     <see cref="GenerateCodeCoverageReports" /> then downloads.
        /// </summary>
        public bool StagedOnWorker { get; init; }
    }

    /// <summary>The outcome of one <c>dotnet test</c>.</summary>
    /// <param name="Name">The sub module's name.</param>
    /// <param name="Project">The project (or solution) tested.</param>
    /// <param name="ResultsDirectory">Where its TRX and coverage files are.</param>
    /// <param name="CommandResult">The command's result, when it ran.</param>
    /// <param name="Error">Why it failed, or <see langword="null" />.</param>
    public record ProjectResult(string Name, FilePath Project, FolderPath ResultsDirectory, CommandResult? CommandResult, string? Error);
}
