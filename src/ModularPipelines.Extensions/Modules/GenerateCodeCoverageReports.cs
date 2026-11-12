using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using ModularPipelines.Caching;
using ModularPipelines.Distributed;
using ModularPipelines.FileSystem;
using Rocket.Surgery.ModularPipelines.Extensions.Mise;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Generates code coverage artifacts from cobertura files emitted by test execution, and from
///     <see cref="TestSettings.CoverageReports" />.
/// </summary>
/// <remarks>
///     When a distributed worker ran <see cref="TestSolution" />, its results arrive as the <see cref="TestSolution.ResultsArtifact" />
///     artifact and are copied into <see cref="TestSettings.TestsDirectory" /> first, so the coordinator also has every TRX file to
///     publish. Outside a worker that artifact is an empty folder.
/// </remarks>
[DependsOn<TestSolution>]
[ConsumesArtifact(typeof(TestSolution), TestSolution.ResultsArtifact, RestorePath = TestSolution.ResultsArtifactPath)]
[RequiresCapability(RocketCapabilities.Coordinator)]
public partial class GenerateCodeCoverageReports
(
    SharedSettings sharedSettings,
    TestSettings testSettings,
    IOptions<DistributedOptions> distributedOptions,
    IOptions<ModuleCacheOptions> moduleCacheOptions) : Module<GenerateCodeCoverageReports.Result>
{
    private const string ReportGeneratorCommandName = "reportgenerator";

    // In a distributed run the reports may still be on their way from a worker, so only the run can tell.
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(distributedOptions.Value.TotalInstances <= 1 && FindInputReports().Count == 0,
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

        var fromWorker = TestSolution.GetResultsArtifactFolder(moduleCacheOptions.Value);
        if (fromWorker.Exists && fromWorker.GetFiles(_ => true).Any())
        {
            await fromWorker.CopyToAsync(testSettings.TestsDirectory, cancellationToken: cancellationToken);
        }

        var inputReports = FindInputReports();
        if (inputReports.Count == 0)
        {
            return new(coverageDirectory, coverageReportDirectory, coverageSummaryDirectory, inputReports);
        }

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

    private ImmutableList<FilePath> FindInputReports() =>
        testSettings
           .TestsDirectory
           .GetFiles(
                x => x.Name.EndsWith(".cobertura.xml", StringComparison.OrdinalIgnoreCase),
                x => x.Name.Equals("merged", StringComparison.OrdinalIgnoreCase)
            )
           .Concat(testSettings.CoverageReports.SelectMany(pattern => sharedSettings.RootDirectory.GetFiles(pattern)))
           .DistinctBy(x => x.Path, StringComparer.Ordinal)
           .ToImmutableList();

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
