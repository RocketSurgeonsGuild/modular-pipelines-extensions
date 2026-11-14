using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using ModularPipelines.Exceptions;
using ModularPipelines.Reporting;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Runs the solution's tests with coverage: by default one <c>dotnet test</c> per test project, each a sub module, several at once,
///     ordered by their history (see <see cref="TestQueue" />); or one <c>dotnet test</c> over the solution, as
///     <see cref="TestSettings.Mode" /> says.
/// </summary>
[DependsOn<RestoreSolutionModule>]
[DependsOn<BuildSolution>]
[DependsOn<SolutionProjectsModule>]
public partial class TestSolutionModule
(
    ArtifactSettings artifactSettings,
    TestSettings testSettings,
    SolutionSettings settings = null!,
    IModuleEstimatedTimeProvider? estimates = null) : Module<TestSolutionModule.Result?>
{
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(_ => SkipDecision.When(
                                                                                                    settings is null || !testSettings.IsEnabled,
                                                                                                    "Tests are disabled or no solution was provided"
                                                                                                )
    );

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        testSettings.TestArtifactsDirectory.Clean();

        if (!testSettings.RunSettings.Exists)
        {
            await using var tempFile = testSettings.RunSettings.GetStream(FileAccess.Write);
            await typeof(TestSolutionModule)
                 .Assembly
                 // ReSharper disable once NullableWarningSuppressionIsUsed
                 .GetManifestResourceStream("Rocket.Surgery.ModularPipelines.Extensions.default.runsettings")!.CopyToAsync(tempFile, cancellationToken);
        }

        var binlog = artifactSettings.LogsDirectory.GetFile("tests.binlog");
        var result = testSettings.Mode == TestMode.Solution
            ? await RunSolutionAsync(context, binlog, cancellationToken)
            : await RunProjectsAsync(context, binlog, cancellationToken);

        return ThrowIfAnyFailed(result);
    }

    [SuppressMessage("ModularPipelines", "MP0016", Justification = "Each call passes the module's token on. MP0016 only recognizes ExecuteAsync's own.")]
    private async Task<Result> RunProjectsAsync(IModuleContext context, FilePath binlog, CancellationToken cancellationToken)
    {
        var projects = ( await context.GetModule<SolutionProjectsModule>() ).ValueOrDefault ?? [];
        var testProjects = projects.Where(project => project.IsTestProject).ToArray();
        WarnAboutUnmatchedPatterns(context, testProjects.Select(project => testSettings.GetRootRelativePath(project.ProjectPath)));
        var excluded = testProjects.Where(project => testSettings.IsExcluded(project.ProjectPath)).ToArray();
        if (excluded.Length > 0 && context.Logger.IsEnabled(LogLevel.Information))
        {
            LogExcluding(context.Logger, excluded.Length, string.Join(", ", excluded.Select(project => testSettings.GetRootRelativePath(project.ProjectPath))));
        }

        var targets = TestQueue.InShard(
                                   TestQueue.Expand(testProjects.Except(excluded), testSettings.PerFramework, testSettings.Frameworks),
                                   testSettings.Shard
                               )
                              .ToArray();
        if (testSettings.Shard is { } shard && context.Logger.IsEnabled(LogLevel.Information))
        {
            LogShard(context.Logger, shard.ToString(), targets.Length);
        }

        var moduleId = ModuleId.FromType(GetType());
        var queue = TestQueue.Order(
            targets,
            await GetEstimatesAsync(context, moduleId, cancellationToken),
            OrderPriority
        );
        var fastLane = TestQueue.UsesFastLane(queue, testSettings.Parallelism);
        if (context.Logger.IsEnabled(LogLevel.Information))
        {
            LogRunningQueue(context.Logger, TestQueue.Describe(queue, testSettings.Parallelism, fastLane));
        }

        var outcomes = new ConcurrentBag<ProjectResult>();
        var lanes = new TestLanes(queue);
        // Each slot takes its next project when it is free: the fast lane, when there is one, the shortest left, the others the longest.
        await Task.WhenAll(
            Enumerable
               .Range(0, Math.Min(testSettings.Parallelism, queue.Length))
               .Select(slot => RunSlotAsync(
                           context,
                           moduleId,
                           lanes,
                           fastLane && slot == 0,
                           outcomes,
                           cancellationToken
                       )
                )
        );

        ImmutableList<ProjectResult> results = [.. outcomes.OrderBy(outcome => outcome.Name, StringComparer.Ordinal)];
        await WriteSummaryAsync(context, results, cancellationToken);
        return new(null, FindTrxFiles(testSettings.TestArtifactsDirectory), binlog, results);
    }

    /// <summary>
    ///     Logs how long each test sub module took against its estimate, longest first, and adds the table to the GitHub Actions job summary
    ///     when there is one: ModularPipelines' own summary lists modules, not sub modules.
    /// </summary>
    private static async Task WriteSummaryAsync(IModuleContext context, IReadOnlyCollection<ProjectResult> results, CancellationToken cancellationToken)
    {
        if (results.Count == 0) return;

        var rows = results
                  .OrderByDescending(result => result.Duration ?? TimeSpan.Zero)
                  .Select(result => (
                              result.Name,
                              Outcome: result.Error is null ? "passed" : "failed",
                              Duration: result.Duration is { } duration ? duration.Humanize(2) : "-",
                              Estimate: result.Estimate is { } estimate ? estimate.Humanize(2) : "-"
                          )
                   )
                  .ToArray();
        if (context.Logger.IsEnabled(LogLevel.Information))
        {
            LogSummary(
                context.Logger,
                string.Join(Environment.NewLine, rows.Select(row => $"  {row.Name}: {row.Outcome} in {row.Duration} (estimated {row.Estimate})"))
            );
        }

        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is not { Length: > 0 } summaryFile) return;

        var markdown = new System.Text.StringBuilder()
                      .AppendLine("### Test projects")
                      .AppendLine()
                      .AppendLine("| Project | Outcome | Duration | Estimate |")
                      .AppendLine("| --- | --- | --- | --- |");
        foreach (var (Name, Outcome, Duration, Estimate) in rows)
        {
            markdown.AppendLine($"| {Name} | {Outcome} | {Duration} | {Estimate} |");
        }

        try
        {
            await File.AppendAllTextAsync(summaryFile, markdown.AppendLine().ToString(), cancellationToken);
        }
        catch (IOException ex)
        {
            context.Logger.LogWarning(ex, "Could not write the test projects to the job summary");
        }
    }

    private async Task RunSlotAsync(
        IModuleContext context,
        ModuleId moduleId,
        TestLanes lanes,
        bool fastLane,
        ConcurrentBag<ProjectResult> outcomes,
        CancellationToken cancellationToken
    )
    {
        // Off the caller's thread, so the slots start together.
        await Task.Yield();
        while (lanes.TryTake(fastLane, out var entry))
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(await RunProjectAsync(context, moduleId, entry, cancellationToken));
        }
    }

    [SuppressMessage(
        "ModularPipelines",
        "MP0016",
        Justification = "Each call passes a token: the slot's from Parallel.ForEachAsync, which ends with the module's. MP0016 only recognizes ExecuteAsync's own."
    )]
    private async Task<ProjectResult> RunProjectAsync(IModuleContext context, ModuleId moduleId, TestQueueEntry entry, CancellationToken cancellationToken)
    {
        var resultsDirectory = testSettings.TestArtifactsDirectory.GetFolder(entry.ResultsFolder);
        var started = Stopwatch.GetTimestamp();
        LogStarting(context.Logger, entry.Name, entry.Estimate is { } estimate ? estimate.Humanize(2) : "no previous run");
        try
        {
            var commandResult = await context.RunSubModuleAsync(
                entry.Name,
                token => context.Tools.DotNet.TestAsync(
                    CreateOptions(resultsDirectory, Collects(entry.EffectiveFramework)) with
                    {
                        Project = entry.Project.ProjectPath.Path,
                        Framework = entry.Framework,
                    },
                    new(),
                    token
                ),
                cancellationToken
            );
            var elapsed = Stopwatch.GetElapsedTime(started);
            LogFinished(context.Logger, entry.Name, "passed", elapsed.Humanize(2));
            return new(entry.Name, entry.Project.ProjectPath, resultsDirectory, commandResult, null, elapsed, entry.Estimate);
        }
        // One failing project must not stop the others; the module fails once they all finished.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ModularPipelines keeps the duration of a sub module that succeeded only, but a failed run took its time as well, and the
            // next run's order should know it.
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (estimates is { })
            {
                await estimates.SaveSubModuleTimeAsync(moduleId, new(entry.Name, elapsed), cancellationToken);
            }

            LogFinished(context.Logger, entry.Name, "failed", elapsed.Humanize(2));
            return new(entry.Name, entry.Project.ProjectPath, resultsDirectory, ( ex as CommandException )?.Result, FirstLine(ex.Message), elapsed, entry.Estimate);
        }
    }

    private async Task<IReadOnlyDictionary<string, TimeSpan>> GetEstimatesAsync(IModuleContext context, ModuleId moduleId, CancellationToken cancellationToken)
    {
        if (estimates is null) return ImmutableDictionary<string, TimeSpan>.Empty;

        try
        {
            return ( await estimates.GetSubModuleEstimatedTimesAsync(moduleId, cancellationToken) )
                  .GroupBy(estimate => estimate.SubModuleName, StringComparer.Ordinal)
                  .ToDictionary(group => group.Key, group => group.Last().EstimatedDuration, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Logger.LogWarning(ex, "Could not read the test projects' previous durations; running them in path order");
            return ImmutableDictionary<string, TimeSpan>.Empty;
        }
    }

    private async Task<Result> RunSolutionAsync(IModuleContext context, FilePath binlog, CancellationToken cancellationToken)
    {
        var solution = await SolutionToTest(context, cancellationToken);
        // One dotnet test over the solution can only choose one framework; per-framework and per-shard runs need PerProject mode.
        var framework = testSettings.Frameworks is [var only] ? only : null;
        try
        {
            var commandResult = await context.Tools.DotNet.TestAsync(
                CreateOptions(testSettings.TestArtifactsDirectory, Collects(framework)) with { Solution = solution, Framework = framework },
                new(),
                cancellationToken
            );
            return new(
                commandResult,
                FindTrxFiles(testSettings.TestArtifactsDirectory),
                binlog,
                [new(settings.Solution.Name, settings.Solution, testSettings.TestArtifactsDirectory, commandResult, null)]
            );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var commandResult = ( ex as CommandException )?.Result;
            return new(
                commandResult,
                FindTrxFiles(testSettings.TestArtifactsDirectory),
                binlog,
                [new(settings.Solution.Name, settings.Solution, testSettings.TestArtifactsDirectory, commandResult, FirstLine(ex.Message))]
            );
        }
    }

    /// <summary>Whether a run of <paramref name="framework" /> collects coverage: one of <see cref="TestSettings.CoverageFrameworks" />, or any when none are named or the run covers several frameworks.</summary>
    private bool Collects(string? framework) =>
        framework is null
     || testSettings.CoverageFrameworks.Count == 0
     || testSettings.CoverageFrameworks.Contains(framework, StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a test project without history goes among the others with none: by the first <see cref="TestSettings.Order" /> pattern it matches.</summary>
    private int OrderPriority(SolutionProjectsModule.Project project)
    {
        var path = testSettings.GetRootRelativePath(project.ProjectPath);
        for (var index = 0; index < testSettings.Order.Count; index++)
        {
            if (TestSettings.Matches(testSettings.Order[index], path)) return index;
        }

        return int.MaxValue;
    }

    private DotNetTestOptions CreateOptions(FolderPath resultsDirectory, bool coverage) =>
        new()
        {
            Configuration = settings.Configuration,
            NoRestore = true,
            NoBuild = true,
            ResultsDirectory = resultsDirectory,
            Arguments =
            [
                .. coverage ? (string[])["--coverage", "--coverage-settings", testSettings.RunSettings, "--coverage-output-format", "cobertura"] : [],
                // One TRX per test application, under its default name: a single --report-trx-filename made every
                // assembly (and target framework) write the same file, so only the last one's results were kept.
                "--report-trx",
                // Without it, dotnet test also merges the TRX files (and the coverage) into merged/, and a reader of
                // every TRX counts each test twice.
                "--no-artifact-post-processing",
            ],
        };

    // One report per test application. --no-artifact-post-processing keeps the .NET 11 SDK from writing merged/; skipping that folder
    // anyway keeps a combined report from being listed alongside the ones it repeats.
    private static ImmutableList<FilePath> FindTrxFiles(FolderPath directory) =>
        directory.Exists
            ?
            [
                .. directory.GetFiles(
                    x => x.Name.EndsWith(".trx", StringComparison.OrdinalIgnoreCase),
                    x => x.Name.Equals("merged", StringComparison.OrdinalIgnoreCase)
                )
            ]
            : [];

    /// <summary>Fails the module, listing every project that failed, once all of them finished.</summary>
    private static Result ThrowIfAnyFailed(Result result) =>
        result.Projects.Where(project => project.Error is not null).ToArray() is { Length: > 0 } failed
            ? throw new InvalidOperationException(
                $"Tests failed in {failed.Length} of {result.Projects.Count} projects:{Environment.NewLine}"
              + string.Join(Environment.NewLine, failed.Select(project => $"  {project.Name}: {project.Error}"))
            )
            : result;

    private static string FirstLine(string message) => message.Split('\n', 2)[0].TrimEnd();

    /// <summary>
    ///     The solution, or, when <see cref="TestSettings.ExcludeTests" /> names projects, a solution filter of it without them, written to the
    ///     tests directory. Only the test run uses the filter: the solution still builds and packs every project.
    /// </summary>
    private async Task<FilePath> SolutionToTest(IModuleContext context, CancellationToken cancellationToken)
    {
        if (testSettings.ExcludeTests.Count == 0) return settings.Solution;

        var serializer = SolutionSerializers.GetSerializerByMoniker(settings.Solution.Path)
         ?? throw new InvalidOperationException($"Not a solution file: {settings.Solution.Path}");
        var model = await serializer.OpenAsync(settings.Solution.Path, cancellationToken);
        var solutionDirectory = Path.GetDirectoryName(settings.Solution.Path)!;

        var included = new List<string>();
        var excluded = new List<string>();
        var all = new List<string>();
        foreach (var project in model.SolutionProjects)
        {
            // the path relative to the solution is what a solution filter lists; the patterns are relative to the root directory
            var projectPath = project.FilePath.Replace('\\', '/');
            var fullPath = new FilePath(Path.GetFullPath(Path.Combine(solutionDirectory, projectPath)));
            all.Add(testSettings.GetRootRelativePath(fullPath));
            if (testSettings.IsExcluded(fullPath))
            {
                excluded.Add(testSettings.GetRootRelativePath(fullPath));
                continue;
            }

            included.Add(projectPath);
        }

        WarnAboutUnmatchedPatterns(context, all);
        if (excluded.Count > 0 && context.Logger.IsEnabled(LogLevel.Information))
        {
            LogExcluding(context.Logger, excluded.Count, string.Join(", ", excluded));
        }

        var filter = testSettings.TestArtifactsDirectory.GetFile($"{Path.GetFileNameWithoutExtension(settings.Solution.Name)}.tests.slnf");
        await using (var stream = filter.GetStream(FileAccess.Write))
        await using (var writer = new Utf8JsonWriter(stream, new() { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("solution");
            writer.WriteString("path", Path.GetRelativePath(testSettings.TestArtifactsDirectory.Path, settings.Solution.Path).Replace('\\', '/'));
            writer.WriteStartArray("projects");
            foreach (var project in included)
            {
                writer.WriteStringValue(project);
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
    /// <param name="Projects">The outcome of each <c>dotnet test</c>: each project, or the solution.</param>
    public record Result(CommandResult? CommandResult, ImmutableList<FilePath> TrxFiles, FilePath Binlog, ImmutableList<ProjectResult> Projects);

    /// <summary>The outcome of one <c>dotnet test</c>.</summary>
    /// <param name="Name">The sub module's name.</param>
    /// <param name="Project">The project (or solution) tested.</param>
    /// <param name="ResultsDirectory">Where its TRX and coverage files are.</param>
    /// <param name="CommandResult">The command's result, when it ran.</param>
    /// <param name="Error">Why it failed, or <see langword="null" />.</param>
    /// <param name="Duration">How long it took, when it ran as a sub module.</param>
    /// <param name="Estimate">What its history said it would take.</param>
    public record ProjectResult(
        string Name,
        FilePath Project,
        FolderPath ResultsDirectory,
        CommandResult? CommandResult,
        string? Error,
        TimeSpan? Duration = null,
        TimeSpan? Estimate = null
    );

    private void WarnAboutUnmatchedPatterns(IModuleContext context, IEnumerable<string> rootRelativePaths)
    {
        var paths = rootRelativePaths.ToArray();
        foreach (var pattern in testSettings.ExcludeTests.Where(pattern => !paths.Any(path => TestSettings.Matches(pattern, path))))
        {
            LogUnmatchedPattern(context.Logger, pattern);
        }
    }

    [LoggerMessage(LogLevel.Information, "Leaving {Count} test projects out of the test run: {Projects}")]
    static partial void LogExcluding(ILogger logger, int count, string projects);

    [LoggerMessage(LogLevel.Warning, "ExcludeTests pattern {Pattern} matches no project in the solution")]
    static partial void LogUnmatchedPattern(ILogger logger, string pattern);

    [LoggerMessage(LogLevel.Information, "Test shard {Shard}: {Count} test sub modules")]
    static partial void LogShard(ILogger logger, string shard, int count);

    [LoggerMessage(LogLevel.Information, "▶ {Name} ({Estimate})")]
    static partial void LogStarting(ILogger logger, string name, string estimate);

    [LoggerMessage(LogLevel.Information, "■ {Name} {Outcome} in {Duration}")]
    static partial void LogFinished(ILogger logger, string name, string outcome, string duration);

    [LoggerMessage(LogLevel.Information, "Test projects, longest first:\n{Summary}")]
    static partial void LogSummary(ILogger logger, string summary);

    [LoggerMessage(LogLevel.Information, "Running {Queue}")]
    static partial void LogRunningQueue(ILogger logger, string queue);
}
