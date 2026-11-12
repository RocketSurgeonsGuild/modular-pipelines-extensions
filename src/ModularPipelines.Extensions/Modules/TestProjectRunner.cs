using System.Collections.Concurrent;
using System.Collections.Immutable;
using ModularPipelines.Exceptions;
using Proto.Promises;
using Proto.Promises.Threading;
using InvalidOperationException = System.InvalidOperationException;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Runs test projects one <c>dotnet test</c> each, as sub modules of the calling module, several at once. Shared by
///     <see cref="TestSolution" /> and the test shards (<see cref="TestShardModule" />).
/// </summary>
internal static class TestProjectRunner
{
    /// <summary>The sub module name of a project, which is also the key its duration estimate is kept under.</summary>
    public static string GetName(SolutionProject project, TestSettings testSettings) =>
        testSettings.TestFramework is { } framework ? $"{project.AssemblyName} ({framework})" : project.AssemblyName;

    /// <summary>The folder a project's TRX and coverage files are written to.</summary>
    public static FolderPath GetResultsDirectory(SolutionProject project, TestSettings testSettings) =>
        testSettings.TestsDirectory.GetFolder(
            testSettings.TestFramework is { } framework ? $"{project.AssemblyName}_{framework}" : project.AssemblyName
        );

    public static async Task<ImmutableList<TestSolution.ProjectResult>> RunProjectsAsync(
        IModuleContext context,
        IReadOnlyList<SolutionProject> projects,
        SolutionSettings settings,
        TestSettings testSettings,
        FilePath runSettings,
        bool isWorker,
        CancellationToken cancellationToken
    )
    {
        var outcomes = new ConcurrentBag<TestSolution.ProjectResult>();
        await ParallelAsync.ForEach(
            projects,
            new ParallelAsyncOptions { CancelationToken = cancellationToken.ToCancelationToken(), MaxDegreeOfParallelism = testSettings.Parallelism },
            async (project, token) =>
            {
                var name = GetName(project, testSettings);
                var resultsDirectory = GetResultsDirectory(project, testSettings);
                try
                {
                    var commandResult = await context.RunSubModuleAsync(
                        name,
                        subToken => context.Tools.DotNet.TestAsync(
                            CreateOptions(settings, testSettings, runSettings, isWorker, resultsDirectory) with { Project = project.Path.Path },
                            new(),
                            subToken
                        ),
                        cancellationToken
                    );
                    outcomes.Add(new(name, project.Path, resultsDirectory, commandResult, null));
                }
                // One failing project must not stop the others; the module fails once they all finished.
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    outcomes.Add(new(name, project.Path, resultsDirectory, ( ex as CommandException )?.Result, FirstLine(ex.Message)));
                }
            }
        );

        return [.. outcomes.OrderBy(outcome => outcome.Name, StringComparer.Ordinal)];
    }

    public static DotNetTestOptions CreateOptions(
        SolutionSettings settings,
        TestSettings testSettings,
        FilePath runSettings,
        bool isWorker,
        FolderPath resultsDirectory
    ) =>
        new()
        {
            Configuration = settings.Configuration,
            Framework = testSettings.TestFramework,
            // A worker did not run the coordinator's restore and build, so its tests build what they run.
            NoRestore = !isWorker,
            NoBuild = !isWorker,
            ResultsDirectory = resultsDirectory,
            Arguments =
            [
                "--coverage",
                "--coverage-settings", runSettings,
                "--coverage-output-format", "cobertura",
                // One TRX per test application, under its default name: a single --report-trx-filename made every assembly (and
                // target framework) write the same file, so only the last one's results were kept.
                "--report-trx",
                // Without it, dotnet test also merges the TRX files (and the coverage) into merged/, and a reader of every TRX counts
                // each test twice.
                "--no-artifact-post-processing",
            ],
        };

    // One report per test application. --no-artifact-post-processing keeps the .NET 11 SDK from writing merged/; skipping that folder
    // anyway keeps a combined report from being listed alongside the ones it repeats.
    public static ImmutableList<FilePath> FindTrxFiles(FolderPath directory) =>
        directory.Exists
            ? [.. directory.GetFiles(
                x => x.Name.EndsWith(".trx", StringComparison.OrdinalIgnoreCase),
                x => x.Name.Equals("merged", StringComparison.OrdinalIgnoreCase)
            )]
            : [];

    /// <summary>Fails the module, listing every project that failed, once all of them finished.</summary>
    public static TestSolution.Result ThrowIfAnyFailed(TestSolution.Result result) =>
        result.Projects.Where(project => project.Error is not null).ToArray() is { Length: > 0 } failed
            ? throw new InvalidOperationException(
                $"Tests failed in {failed.Length} of {result.Projects.Count} projects:{Environment.NewLine}"
              + string.Join(Environment.NewLine, failed.Select(project => $"  {project.Name}: {project.Error}"))
            )
            : result;

    public static string FirstLine(string message) => message.Split('\n', 2)[0].TrimEnd();

    // Several shards can run in one worker process; one restore of the solution serves them all.
    private static readonly ConcurrentDictionary<string, Lazy<Task>> Restores = new(StringComparer.Ordinal);

    /// <summary>Restores the solution once per process, for a worker, which did not run the coordinator's restore.</summary>
    public static Task RestoreOnceAsync(IModuleContext context, FilePath solution, CancellationToken cancellationToken)
    {
        var restore = Restores.GetOrAdd(
            solution.Path,
            _ => new(() => context.Tools.DotNet.RestoreAsync(new() { ProjectSolution = solution, IgnoreFailedSources = true }, new(), CancellationToken.None))
        );
        return restore.Value.WaitAsync(cancellationToken);
    }
}
