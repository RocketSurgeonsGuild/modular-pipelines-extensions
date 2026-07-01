using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[DependsOn<RestoreSolution>]
[DependsOn<BuildSolution>]
public partial class TestSolution
(
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

        var binlog = artifactSettings.LogsDirectory.GetFile("tests.binlog");
        var trx = testSettings.TestsDirectory.GetFile("tests.trx");
        var result = await context
                          .DotNet()
                          .Test(
                               new()
                               {
                                   Solution = settings.Solution,
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

    public record Result(CommandResult CommandResult, File Trx, File Binlog);
}
