using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[DependsOn<RestoreSolution>]
[DependsOn<BuildSolution>]
public partial class TestSolution
(
    SharedSettings sharedSettings,
    ArtifactSettings artifactSettings,
    TestSettings testSettings,
    SolutionSettings settings = null!) : Module<TestSolution.Result?>
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

        var solution = await SolutionToTest(context, cancellationToken);
        var binlog = artifactSettings.LogsDirectory.GetFile("tests.binlog");
        var result = await context
                          .Tools
                          .DotNet
                          .TestAsync(
                               new()
                               {
                                   Solution = solution,
                                   Configuration = settings.Configuration,
                                   NoRestore = true,
                                   NoBuild = true,
                                   ResultsDirectory = testSettings.TestsDirectory,
                                   // ConfigFile = testSettings.RunSettings,
                                   Arguments =
                                   [
                                       "--coverage",
                                       "--coverage-settings", testSettings.RunSettings,
                                       //  "--coverage-output", testSettings.CoverageDirectory.GetFile("coverage.cobertura"),
                                       "--coverage-output-format", "cobertura",
                                       // One TRX per test application, under its default name: a single --report-trx-filename made every
                                       // assembly (and target framework) write the same file, so only the last one's results were kept.
                                       "--report-trx",
                                       // Without it, dotnet test also merges the TRX files (and the coverage) into merged/, and a reader of
                                       // every TRX counts each test twice.
                                       "--no-artifact-post-processing",
                                   ],
                                   // TODO: Add verbosity, and loggers.
                                   // Arguments
                               },
                               new(),
                               cancellationToken
                           );

        // One report per module. --no-artifact-post-processing keeps the .NET 11 SDK from writing merged/; skipping that folder
        // anyway keeps a combined report from being listed alongside the ones it repeats.
        var trxFiles = testSettings
                      .TestsDirectory
                      .GetFiles(
                           x => x.Name.EndsWith(".trx", StringComparison.OrdinalIgnoreCase),
                           x => x.Name.Equals("merged", StringComparison.OrdinalIgnoreCase)
                       )
                      .ToImmutableList();

        return new(result, trxFiles, binlog);
    }

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
            var patterns = testSettings.ExcludeTests.Where(pattern => Matches(pattern, fromRoot)).ToArray();
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

    private static bool Matches(string pattern, string path)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern);
        return matcher.Match(path).HasMatches;
    }


    /// <summary>The outcome of the test run.</summary>
    /// <param name="CommandResult">The result of the <c>dotnet test</c> command.</param>
    /// <param name="TrxFiles">The TRX report of each test module (one per test project and target framework).</param>
    /// <param name="Binlog">The binary log location.</param>
    public record Result(CommandResult CommandResult, ImmutableList<FilePath> TrxFiles, FilePath Binlog);
}
