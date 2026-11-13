namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Copies the coverage badges <see cref="GenerateCodeCoverageReportsModule" /> made into the docs site <see cref="DocsModule" /> built,
///     under <c>coverage-badges/</c>, so a docs page can reference them.
/// </summary>
/// <remarks>
///     A module of its own so the docs build starts right after the solution is built instead of waiting for the tests and their
///     coverage. The badges go next to the built site, not into the docs' sources before the build, so this works whichever static site
///     generator built it. A consumer without tests, or with <c>EnableTests: false</c>, still gets the docs, without badges.
/// </remarks>
[DependsOn<DocsModule>(Optional = true)]
[DependsOn<GenerateCodeCoverageReportsModule>(Optional = true)]
public class CopyCoverageBadgesModule : Module<FolderPath?>
{
    protected override async Task<FolderPath?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var docsModule = context.GetModuleIfRegistered<DocsModule>();
        var docs = docsModule is null ? null : ( await docsModule ).ValueOrDefault;
        var coverageModule = context.GetModuleIfRegistered<GenerateCodeCoverageReportsModule>();
        var coverage = coverageModule is null ? null : ( await coverageModule ).ValueOrDefault;
        if (docs is null || coverage is not { CoverageBadgeDirectory.Exists: true }) return null;

        var badges = new FolderPath(docs.OutputPath) / "coverage-badges";
        await coverage.CoverageBadgeDirectory.CopyToAsync(badges, cancellationToken: cancellationToken);
        return badges;
    }
}
