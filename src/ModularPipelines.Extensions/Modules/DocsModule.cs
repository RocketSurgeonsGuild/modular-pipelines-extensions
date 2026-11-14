using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.ModularPipelines.Extensions.Mise;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

// The API reference is generated from the build's output, so the docs only build after the solution built successfully. They do not
// wait for the tests: CopyCoverageBadgesModule adds the coverage badges to the built site once the reports exist.
[DependsOn<BuildSolution>]
public class DocsModule(DocsModule.Settings settings, ArtifactSettings artifactSettings) : Module<DocsModule.Result?>
{

    protected override void Configure(ModuleConfigurationBuilder module)
    {
        module.WithSkipWhen(
            _ => SkipDecision.When(!settings.DocsEnabled || !settings.DocsDirectory.Exists,
                "Docs directory does not exist, skipping docs build")
        );
        // Optional, so a pipeline without tests (or with them disabled) still builds the docs.
        if (settings.AfterTests) module.DependsOnOptional<TestSolutionModule>();
    }

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {

        var result = await context.Tools.Mise.Run(
            settings.DocsBuildTask,
            new() { WorkingDirectory = settings.DocsDirectory },
            cancellationToken
        );

        var docsOutputDirectory = artifactSettings.ArtifactsDirectory / "docs";
        await settings.DocsOutputDirectory.CopyToAsync(docsOutputDirectory, cancellationToken: cancellationToken);

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

        /// <summary>
        ///     Whether the docs build waits for the tests instead of running alongside them. Read from <c>Docs:AfterTests</c>; off by
        ///     default. On a small runner a heavy docs build can slow the longest test project by about as much as running it alongside
        ///     saves, so waiting keeps the processors for the tests.
        /// </summary>
        public bool AfterTests { get; } = configuration.GetValue("Docs:AfterTests", false);
    }
}
