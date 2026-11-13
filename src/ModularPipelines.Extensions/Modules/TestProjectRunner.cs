using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using ModularPipelines.Exceptions;
using Proto.Promises;
using Proto.Promises.Threading;
using Rocket.Surgery.ModularPipelines.Extensions.History;
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
        TestProjectHistory? history,
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
                var started = Stopwatch.GetTimestamp();
                TestSolution.ProjectResult outcome;
                try
                {
                    var commandResult = await context.RunSubModuleAsync(
                        name,
                        subToken => context.Tools.DotNet.TestAsync(
                            CreateOptions(settings, testSettings, runSettings, resultsDirectory) with { Project = project.Path.Path },
                            new(),
                            subToken
                        ),
                        cancellationToken
                    );
                    outcome = new(name, project.Path, resultsDirectory, commandResult, null);
                }
                // One failing project must not stop the others; the module fails once they all finished.
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    outcome = new(name, project.Path, resultsDirectory, ( ex as CommandException )?.Result, FirstLine(ex.Message));
                }

                outcomes.Add(outcome);
                // A failed run is recorded too: it is still the project's last run, which the next split of the test shards goes by.
                if (history is not null)
                {
                    await history.RecordAsync(
                        new(name, Stopwatch.GetElapsedTime(started), outcome.Error is null, DateTimeOffset.UtcNow),
                        cancellationToken
                    );
                }
            }
        );

        return [.. outcomes.OrderBy(outcome => outcome.Name, StringComparer.Ordinal)];
    }

    public static DotNetTestOptions CreateOptions(
        SolutionSettings settings,
        TestSettings testSettings,
        FilePath runSettings,
        FolderPath resultsDirectory
    ) =>
        new()
        {
            Configuration = settings.Configuration,
            Framework = testSettings.TestFramework,
            NoRestore = true,
            NoBuild = true,
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
}
