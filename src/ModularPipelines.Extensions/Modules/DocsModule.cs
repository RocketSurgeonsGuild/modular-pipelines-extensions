using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;
using Rocket.Surgery.ModularPipelines.Extensions.Mise;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

// The API reference is generated from the build's output, so the docs only build after the solution built successfully.
[DependsOn<BuildSolution>]
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

        await settings.DocsOutputDirectory.CopyToAsync(artifactSettings.ArtifactsDirectory / "docs", cancellationToken: cancellationToken);

        return result.ExitCode == 0 ? new Result(artifactSettings.ArtifactsDirectory / "docs") : null;
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
