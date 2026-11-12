using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using File = ModularPipelines.FileSystem.File;

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
    protected override ModuleConfiguration Configure() => ModuleConfiguration
                                                         .Create()
                                                         .WithSkipWhen(() => SkipDecision.Of(settings is null || !testSettings.IsEnabled, "Tests are disabled or no solution was provided"))
                                                         .Build();

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        if (!testSettings.RunSettings.Exists)
        {
            await using var tempFile = testSettings.RunSettings.GetStream(FileAccess.Write);
            await typeof(TestSolution)
                 .Assembly
                 // ReSharper disable once NullableWarningSuppressionIsUsed
                 .GetManifestResourceStream("Rocket.Surgery.ModularPipelines.Extensions.default.runsettings")!.CopyToAsync(tempFile, cancellationToken);
        }

        testSettings.TestsDirectory.Clean();

        var solution = await SolutionToTest(context, cancellationToken);
        var binlog = artifactSettings.LogsDirectory.GetFile("tests.binlog");
        var trx = testSettings.TestsDirectory.GetFile("tests.trx");
        var result = await context
                          .DotNet()
                          .Test(
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
                                       "--report-trx",
                                       "--report-trx-filename", trx.Name,
                                   ],
                                   // TODO: Add verbosity, and loggers.
                                   // Arguments
                               },
                               new(),
                               cancellationToken
                           );

        return new(result, trx, binlog);
    }

    /// <summary>
    ///     The solution, or, when <see cref="TestSettings.ExcludeTests" /> names projects, a solution filter of it without them, written to the
    ///     tests directory. Only the test run uses the filter: the solution still builds and packs every project.
    /// </summary>
    private async Task<File> SolutionToTest(IModuleContext context, CancellationToken cancellationToken)
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

    public record Result(CommandResult CommandResult, File Trx, File Binlog);
}
