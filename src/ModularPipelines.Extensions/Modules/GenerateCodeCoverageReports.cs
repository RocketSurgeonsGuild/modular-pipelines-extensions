using System.Collections.Immutable;
using Rocket.Surgery.ModularPipelines.Extensions.Mise;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Generates code coverage artifacts from cobertura files emitted by test execution.
/// </summary>
[DependsOn<TestSolution>]
public partial class GenerateCodeCoverageReports
(TestSettings testSettings) : Module<GenerateCodeCoverageReports.Result>
{
    private const string ReportGeneratorCommandName = "reportgenerator";

    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(!HasInputReports(),
            "No cobertura coverage reports were found")
    );

    /// <summary>
    ///     Executes report generation for coverage artifacts after tests complete.
    /// </summary>
    /// <param name="context">The current module context.</param>
    /// <param name="cancellationToken">The cancellation token for command execution.</param>
    /// <returns>The generated report locations and discovered input report files.</returns>
    /// <exception cref="InvalidOperationException">Thrown when reportgenerator is not installed in the local tool manifest.</exception>
    protected override async Task<Result> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var coverageDirectory = await testSettings.CoverageDirectory.CreateAsync(cancellationToken);
        var coverageReportDirectory = coverageDirectory.GetFolder("report");
        var coverageBadgeDirectory = coverageDirectory.GetFolder("badges");
        var coverageSummaryDirectory = coverageDirectory.GetFolder("summary");

        var inputReports = testSettings
                          .TestsDirectory
                          .GetFiles(x => x.Name.EndsWith(".cobertura.xml", StringComparison.OrdinalIgnoreCase))
                          .ToImmutableList();

        var reportsArgument = string.Join(';', inputReports.Select(x => x.Path));

        // Each run parses every input report, which is most of its time, and they write to separate folders, so they run
        // side by side; this module is the last step of the pipeline.
        await Task.WhenAll(
            RunReportGeneratorAsync(
                context,
                reportsArgument,
                coverageDirectory.Path,
                "Cobertura;lcov",
                cancellationToken
            ),
            RunReportGeneratorAsync(
                context,
                reportsArgument,
                coverageReportDirectory.Path,
                "Html_Dark",
                cancellationToken
            ),
            RunReportGeneratorAsync(
                context,
                reportsArgument,
                coverageSummaryDirectory.Path,
                "Badges;HtmlSummary;TextSummary;MarkdownSummary;MarkdownSummaryGithub",
                cancellationToken
            )
        );

        var coberturaFile = coverageDirectory.GetFile("Cobertura.xml");
        var normalizedCoberturaFile = coverageDirectory.GetFile("test.cobertura.xml");
        if (coberturaFile.Exists)
        {
            if (normalizedCoberturaFile.Exists) normalizedCoberturaFile.Delete();

            coberturaFile.MoveTo(normalizedCoberturaFile);
        }

        return new(
            coverageDirectory,
            coverageReportDirectory,
            coverageSummaryDirectory,
            inputReports
        );
    }

    private static Task<CommandResult> RunReportGeneratorAsync(
        IModuleContext context,
        string reportsArgument,
        string targetDirectory,
        string reportTypes,
        CancellationToken cancellationToken
    ) => context
        .Tools
        .Mise
        .Execute(
             [
                 ReportGeneratorCommandName,
                 $"-reports:{reportsArgument}",
                 $"-targetdir:{targetDirectory}",
                 $"-reporttypes:{reportTypes}",
             ],
             new(),
             cancellationToken
         );

    private bool HasInputReports() =>
        testSettings
           .TestsDirectory
           .GetFiles(x => x.Name.EndsWith(".cobertura.xml", StringComparison.OrdinalIgnoreCase))
           .Any();

    /// <summary>
    ///     The result of generating code coverage reports.
    /// </summary>
    /// <param name="CoverageDirectory">The directory containing consolidated coverage artifacts.</param>
    /// <param name="CoverageReportDirectory">The directory containing the generated HTML report.</param>
    /// <param name="CoverageSummaryDirectory">The directory containing generated summary outputs.</param>
    /// <param name="InputReports">The discovered cobertura input files used as report sources.</param>
    public record Result
    (
        FolderPath CoverageDirectory,
        FolderPath CoverageReportDirectory,
        FolderPath CoverageSummaryDirectory,
        ImmutableList<FilePath> InputReports);
}
