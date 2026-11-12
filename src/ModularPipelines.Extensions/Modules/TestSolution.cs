using System.Collections.Immutable;
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
                                       // No --report-trx-filename: one name for every test module made each module overwrite the
                                       // last one's report, leaving a single TRX. The default name ({asm}_{tfm}_{arch}.trx since
                                       // MTP 2.3, a timestamped one before) is unique per module.
                                       "--report-trx",
                                   ],
                                   // TODO: Add verbosity, and loggers.
                                   // Arguments
                               },
                               new(),
                               cancellationToken
                           );

        return new(result, [.. testSettings.TestsDirectory.GetFiles(z => z.Name.EndsWith(".trx", StringComparison.OrdinalIgnoreCase))], binlog);
    }

    public record Result(CommandResult CommandResult, ImmutableList<File> TrxReports, File Binlog);
}
