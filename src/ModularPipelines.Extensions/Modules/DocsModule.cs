using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;
using Rocket.Surgery.ModularPipelines.Extensions.Mise;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[DependsOn<BuildSolution>(Optional = true)]
public class DocsModule(DocsModule.Settings settings, ArtifactSettings artifactSettings) : Module<DocsModule.Result>
{

    protected override ModuleConfiguration Configure() => ModuleConfiguration
                                                         .Create()
                                                         .WithSkipWhen(() => SkipDecision.Of(!settings.DocsEnabled || !settings.DocsDirectory.Exists, "Docs directory does not exist, skipping docs build"))
                                                         .Build();

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {

        var result = await context.Mise().Run(
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
        public Folder DocsDirectory => field ??= configuration.GetValue<Folder?>("Docs:Directory") ?? sharedSettings.RootDirectory / "docs";
        public string DocsBuildTask => field ??= configuration.GetValue<string>("Docs:Task") ?? "docs:build";
        public Folder DocsOutputDirectory => field ??= configuration.GetValue<Folder?>("Docs:Output") ?? DocsDirectory / "dist";
    }
}
