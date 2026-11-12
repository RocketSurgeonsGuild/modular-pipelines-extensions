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
                                       // No --report-trx-filename: one fixed name is shared by every test module, so with
                                       // several projects or target frameworks each report overwrote the one before. The
                                       // default name is unique per module ({asm}_{tfm}_{arch}.trx since MTP 2.3).
                                       "--report-trx",
                                   ],
                                   // TODO: Add verbosity, and loggers.
                                   // Arguments
                               },
                               new(),
                               cancellationToken
                           );

        // One report per module. With the .NET 11 SDK, dotnet test also writes a combined report of all modules under
        // merged/; it's left out here so the same results aren't listed twice.
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
    /// 
    /// </summary>
    /// <param name="CommandResult">The result of the <c>dotnet test</c> command.</param>
    /// <param name="TrxFiles">The TRX report of each test module (one per test project and target framework).</param>
    /// <param name="Binlog">The binary log location.</param>
    public record Result(CommandResult CommandResult, ImmutableList<File> TrxFiles, File Binlog);
}
