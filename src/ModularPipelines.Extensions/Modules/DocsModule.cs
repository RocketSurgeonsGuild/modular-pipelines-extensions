using Rocket.Surgery.ModularPipelines.Extensions.Mise;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

public class DocsModule(DocsSettings settings, ArtifactSettings artifactSettings) : Module<DocsModule.Result>
{
    public record Result(string OutputPath);

    protected override ModuleConfiguration Configure() => ModuleConfiguration
                                                         .Create()
                                                         .WithSkipWhen(ctx => settings.DocsDirectory.Exists)
                                                         .Build();

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {

        var result = await context.Mise().Execute(
            [settings.DocsBuildTask],
            new() { WorkingDirectory = settings.DocsDirectory },
            cancellationToken
        );

        await settings.DocsOutputDirectory.CopyToAsync(artifactSettings.ArtifactsDirectory / "docs", cancellationToken: cancellationToken);

        return result.ExitCode == 0 ? new Result(artifactSettings.ArtifactsDirectory / "docs") : null;
    }
}
