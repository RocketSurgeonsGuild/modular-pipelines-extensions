using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.ModularPipelines.Extensions.Mise;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Builds the docs site with its mise task and copies the output to <c>docs</c> under the artifacts folder. Skipped when the docs are
///     disabled or the docs folder does not exist.
/// </summary>
/// <param name="settings">Where the docs are and how to build them.</param>
/// <param name="artifactSettings">Where the built site goes.</param>
// The API reference is generated from the build's output, so the docs only build after the solution built successfully. They do not
// wait for the tests: CopyCoverageBadgesModule adds the coverage badges to the built site once the reports exist.
[DependsOn<BuildSolution>]
public class DocsModule(DocsModule.Settings settings, ArtifactSettings artifactSettings) : Module<DocsModule.Result?>
{

    /// <inheritdoc />
    protected override void Configure(ModuleConfigurationBuilder module)
    {
        module.WithSkipWhen(
            _ => SkipDecision.When(!settings.DocsEnabled || !settings.DocsDirectory.Exists,
                "Docs directory does not exist, skipping docs build")
        );
        // Optional, so a pipeline without tests (or with them disabled) still builds the docs.
        if (settings.AfterTests) module.DependsOnOptional<TestSolutionModule>();
    }

    /// <inheritdoc />
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


    /// <summary>The built docs site.</summary>
    /// <param name="OutputPath">The folder the site was copied to.</param>
    public record Result(string OutputPath);
    /// <summary>Where the docs are and how to build them, from the <c>Docs</c> configuration section.</summary>
    /// <param name="sharedSettings">The settings shared by every module.</param>
    /// <param name="configuration">The pipeline configuration.</param>
    [ServiceRegistration(ServiceLifetime.Singleton)]
    public class Settings(SharedSettings sharedSettings, IConfiguration configuration)
    {
        /// <summary>Whether the docs build. Read from <c>Docs:Enabled</c>; on by default.</summary>
        public bool DocsEnabled { get; } = configuration.GetValue("Docs:Enabled", true);
        /// <summary>The docs folder. Read from <c>Docs:Directory</c>; <c>docs</c> under the root directory by default.</summary>
        public FolderPath DocsDirectory => field ??= configuration.GetValue<FolderPath?>("Docs:Directory") ?? sharedSettings.RootDirectory / "docs";
        /// <summary>The mise task that builds the docs. Read from <c>Docs:Task</c>; <c>docs:build</c> by default.</summary>
        public string DocsBuildTask => field ??= configuration.GetValue<string>("Docs:Task") ?? "docs:build";
        /// <summary>The folder the docs build writes the site to. Read from <c>Docs:Output</c>; <c>dist</c> under <see cref="DocsDirectory" /> by default.</summary>
        public FolderPath DocsOutputDirectory => field ??= configuration.GetValue<FolderPath?>("Docs:Output") ?? DocsDirectory / "dist";

        /// <summary>
        ///     Whether the docs build waits for the tests instead of running alongside them. Read from <c>Docs:AfterTests</c>; off by
        ///     default. On a small runner a heavy docs build can slow the longest test project by about as much as running it alongside
        ///     saves, so waiting keeps the processors for the tests.
        /// </summary>
        public bool AfterTests { get; } = configuration.GetValue("Docs:AfterTests", false);
    }
}
