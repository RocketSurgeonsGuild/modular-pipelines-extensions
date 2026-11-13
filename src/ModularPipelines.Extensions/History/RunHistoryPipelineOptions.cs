using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using ModularPipelines.Options;
using ModularPipelines.Reporting;

namespace Rocket.Surgery.ModularPipelines.Extensions.History;

/// <summary>
///     Applies the <c>History</c> section (<see cref="RocketHistoryOptions" />) to ModularPipelines' run report options when the pipeline
///     first resolves <see cref="IOptions{TOptions}" /> of <see cref="PipelineOptions" />, which applies every
///     <see cref="IConfigureOptions{TOptions}" /> registered as a service.
/// </summary>
internal sealed class RunHistoryPipelineOptions(IOptions<RocketHistoryOptions> historyOptions, SharedSettings sharedSettings)
    : IConfigureOptions<PipelineOptions>
{
    public void Configure(PipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var history = historyOptions.Value;
        var root = sharedSettings.RootDirectory.Path;
        var runReport = options.RunReport;

        SetRunReport(
            options,
            history.Enabled
                ? runReport with
                {
                    HistoryDirectory = Path.GetFullPath(history.Directory, root),
                    HistoryRetention = history.Retention,
                    GlobalHistoryRetention = runReport.GlobalHistoryRetention > 0
                        ? Math.Max(runReport.GlobalHistoryRetention, history.Retention)
                        : 0,
                    PipelineIdentity = history.Identity ?? DefaultIdentity(root),
                }
                : runReport with { HistoryRetention = 0 }
        );
    }

    internal static string DefaultIdentity(string root)
    {
        var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (string.IsNullOrWhiteSpace(repository)) return Path.GetFileName(Path.TrimEndingDirectorySeparator(root));

        var workflow = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW");
        return string.IsNullOrWhiteSpace(workflow) ? repository : $"{repository}/{workflow}";
    }

    // PipelineOptions is a record of init-only properties, meant to be replaced through PipelineBuilder.ConfigureOptions, while
    // IConfigureOptions gets the instance to change. The accessor calls the init accessor directly, without reflection.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_RunReport")]
    private static extern void SetRunReport(PipelineOptions options, RunReportOptions value);
}
