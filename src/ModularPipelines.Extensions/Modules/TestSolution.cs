using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
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
[DependsOn<RestoreSolution>]
[DependsOn<BuildSolution>]
public partial class TestSolution
(
    SharedSettings sharedSettings,
    ArtifactSettings artifactSettings,
    TestSettings testSettings,
    SolutionSettings settings = null!,
    IModuleEstimatedTimeProvider? estimates = null) : Module<TestSolution.Result?>
{
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(settings is null || !testSettings.IsEnabled,
            "Tests are disabled or no solution was provided")
    );

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        // Cleaned first: the default settings are written into this folder. They are written on every run (not only when
        // missing) so a newer library's defaults replace an older copy.
        testSettings.TestsDirectory.Clean();

        if (testSettings.UsesDefaultRunSettings || !testSettings.RunSettings.Exists)
        {
            await using var tempFile = testSettings.RunSettings.GetStream(FileAccess.Write);
            await typeof(TestSolution)
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
        var projects = await SolutionProjects.EvaluateAsync(context, sharedSettings, settings.Solution, cancellationToken);
        SolutionProjects.WarnAboutUnmatchedPatterns(context, projects, testSettings);
        var excluded = projects.Where(project => project.IsTestProject && SolutionProjects.IsExcluded(project, testSettings)).ToArray();
        if (excluded.Length > 0 && context.Logger.IsEnabled(LogLevel.Information))
        {
            context.Logger.LogInformation("Leaving {Count} test projects out of the test run: {Projects}", excluded.Length, string.Join(", ", excluded.Select(project => project.RootPath)));
        }

        var moduleId = ModuleId.FromType(GetType());
        var queue = TestQueue.Order(
            projects.Where(project => project.IsTestProject).Except(excluded),
            await GetEstimatesAsync(context, moduleId, cancellationToken)
        );
        var fastLane = TestQueue.UsesFastLane(queue, testSettings.Parallelism);
        if (context.Logger.IsEnabled(LogLevel.Information))
        {
            context.Logger.LogInformation("Running {Queue}", TestQueue.Describe(queue, testSettings.Parallelism, fastLane));
        }

        var outcomes = new ConcurrentBag<ProjectResult>();
        var lanes = new TestLanes(queue);
        // Each slot takes its next project when it is free: the fast lane, when there is one, the shortest left, the others the longest.
        await Task.WhenAll(
            Enumerable
               .Range(0, Math.Min(testSettings.Parallelism, queue.Length))
               .Select(slot => RunSlotAsync(context, moduleId, lanes, fastLane && slot == 0, outcomes, cancellationToken))
        );

        return new(null, FindTrxFiles(testSettings.TestsDirectory), binlog, [.. outcomes.OrderBy(outcome => outcome.Name, StringComparer.Ordinal)]);
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

    [SuppressMessage("ModularPipelines", "MP0016", Justification = "Each call passes a token: the slot's from Parallel.ForEachAsync, which ends with the module's. MP0016 only recognizes ExecuteAsync's own.")]
    private async Task<ProjectResult> RunProjectAsync(IModuleContext context, ModuleId moduleId, TestQueueEntry entry, CancellationToken cancellationToken)
    {
        var resultsDirectory = testSettings.TestsDirectory.GetFolder(entry.Name);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var commandResult = await context.RunSubModuleAsync(
                entry.Name,
                token => context.Tools.DotNet.TestAsync(CreateOptions(resultsDirectory) with { Project = entry.Project.Path.Path }, new(), token),
                cancellationToken
            );
            return new(entry.Name, entry.Project.Path, resultsDirectory, commandResult, null);
        }
        // One failing project must not stop the others; the module fails once they all finished.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ModularPipelines keeps the duration of a sub module that succeeded only, but a failed run took its time as well, and the
            // next run's order should know it.
            if (estimates is not null)
            {
                await estimates.SaveSubModuleTimeAsync(moduleId, new(entry.Name, Stopwatch.GetElapsedTime(started)), cancellationToken);
            }

            return new(entry.Name, entry.Project.Path, resultsDirectory, ( ex as CommandException )?.Result, FirstLine(ex.Message));
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
        try
        {
            var commandResult = await context.Tools.DotNet.TestAsync(CreateOptions(testSettings.TestsDirectory) with { Solution = solution }, new(), cancellationToken);
            return new(commandResult, FindTrxFiles(testSettings.TestsDirectory), binlog, [new(settings.Solution.Name, settings.Solution, testSettings.TestsDirectory, commandResult, null)]);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var commandResult = ( ex as CommandException )?.Result;
            return new(commandResult, FindTrxFiles(testSettings.TestsDirectory), binlog, [new(settings.Solution.Name, settings.Solution, testSettings.TestsDirectory, commandResult, FirstLine(ex.Message))]);
        }
    }

    private DotNetTestOptions CreateOptions(FolderPath resultsDirectory) =>
        new()
        {
            Configuration = settings.Configuration,
            NoRestore = true,
            NoBuild = true,
            ResultsDirectory = resultsDirectory,
            Arguments =
            [
                "--coverage",
                "--coverage-settings", testSettings.RunSettings,
                "--coverage-output-format", "cobertura",
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
            ? [.. directory.GetFiles(
                x => x.Name.EndsWith(".trx", StringComparison.OrdinalIgnoreCase),
                x => x.Name.Equals("merged", StringComparison.OrdinalIgnoreCase)
            )]
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
        var unmatched = testSettings.ExcludeTests.ToHashSet(StringComparer.Ordinal);
        foreach (var project in model.SolutionProjects)
        {
            // the path relative to the solution is what a solution filter lists; the patterns are relative to the root directory
            var projectPath = project.FilePath.Replace('\\', '/');
            var fromRoot = Path.GetRelativePath(sharedSettings.RootDirectory.Path, Path.GetFullPath(Path.Combine(solutionDirectory, projectPath))).Replace('\\', '/');
            var patterns = testSettings.ExcludeTests.Where(pattern => SolutionProjects.Matches(pattern, fromRoot)).ToArray();
            if (patterns.Length == 0)
            {
                included.Add(projectPath);
                continue;
            }

            excluded.Add(fromRoot);
            unmatched.ExceptWith(patterns);
        }

        foreach (var pattern in unmatched)
        {
            context.Logger.LogWarning("ExcludeTests pattern {Pattern} matches no project in {Solution}", pattern, settings.Solution.Name);
        }

        if (context.Logger.IsEnabled(LogLevel.Information))
        {
            context.Logger.LogInformation("Leaving {Count} projects out of the test run: {Projects}", excluded.Count, string.Join(", ", excluded));
        }

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
    public record ProjectResult(string Name, FilePath Project, FolderPath ResultsDirectory, CommandResult? CommandResult, string? Error);
}
