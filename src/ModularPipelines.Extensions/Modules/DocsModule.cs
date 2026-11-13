using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.ModularPipelines.Extensions.Mise;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

// The API reference is generated from the build's output, so the docs only build after the solution built successfully. Coverage
// badges are optional: a consumer without tests, or with EnableTests: false, still gets a docs build.
[DependsOn<BuildSolution>]
[DependsOn<GenerateCodeCoverageReportsModule>(Optional = true)]
public class DocsModule(DocsModule.Settings settings, ArtifactSettings artifactSettings) : Module<DocsModule.Result?>
{

    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(!settings.DocsEnabled || !settings.DocsDirectory.Exists,
            "Docs directory does not exist, skipping docs build")
    );

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {

        var result = await context.Tools.Mise.Run(
            settings.DocsBuildTask,
            new() { WorkingDirectory = settings.DocsDirectory },
            cancellationToken
        );

        var docsOutputDirectory = artifactSettings.ArtifactsDirectory / "docs";
        await settings.DocsOutputDirectory.CopyToAsync(docsOutputDirectory, cancellationToken: cancellationToken);

        // Published alongside the built site, not into DocsOutputDirectory before the build, so this works regardless of which
        // static site generator produced it and how it handles its own static-asset folder.
        var coverageModule = context.GetModuleIfRegistered<GenerateCodeCoverageReportsModule>();
        var coverage = coverageModule is null ? null : ( await coverageModule ).ValueOrDefault;
        if (coverage is { CoverageBadgeDirectory.Exists: true })
        {
            await coverage.CoverageBadgeDirectory.CopyToAsync(docsOutputDirectory / "coverage-badges", cancellationToken: cancellationToken);
        }

        return result.ExitCode == 0 ? new Result(docsOutputDirectory) : null;
    }


    public record Result(string OutputPath);
    [ServiceRegistration(ServiceLifetime.Singleton)]
    public class Settings(SharedSettings sharedSettings, IConfiguration configuration)
    {
        public bool DocsEnabled { get; } = configuration.GetValue("Docs:Enabled", true);
        public FolderPath DocsDirectory => field ??= configuration.GetValue<FolderPath?>("Docs:Directory") ?? sharedSettings.RootDirectory / "docs";
        public string DocsBuildTask => field ??= configuration.GetValue<string>("Docs:Task") ?? "docs:build";
        public FolderPath DocsOutputDirectory => field ??= configuration.GetValue<FolderPath?>("Docs:Output") ?? DocsDirectory / "dist";
    }
}
