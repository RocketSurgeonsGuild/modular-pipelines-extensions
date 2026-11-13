using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.ModularPipelines.Extensions.Mise;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

// The API reference is generated from the build's output, so the docs only build after the solution built successfully.
[DependsOn<BuildSolution>]
public class DocsModule(DocsModule.Settings settings, ArtifactSettings artifactSettings) : Module<DocsModule.Result?>
{

    // A failed docs build is reported but, by default, does not fail the run: it used to cancel the tests running beside it.
    protected override void Configure(ModuleConfigurationBuilder module) => module
       .WithSkipWhen(
            _ => SkipDecision.When(!settings.DocsEnabled || !settings.DocsDirectory.Exists,
                "Docs directory does not exist, skipping docs build")
        )
       .WithIgnoreFailuresWhen((_, _) => settings.IgnoreFailures);

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {

        var result = await context.Tools.Mise.Run(
            settings.DocsBuildTask,
            new() { WorkingDirectory = settings.DocsDirectory, EnvironmentVariables = GetEnvironmentVariables() },
            cancellationToken
        );

        await settings.DocsOutputDirectory.CopyToAsync(artifactSettings.ArtifactsDirectory / "docs", cancellationToken: cancellationToken);

        return result.ExitCode == 0 ? new Result(artifactSettings.ArtifactsDirectory / "docs") : null;
    }


    // The docs build reads the GitHub API (releases, contributors), which rate limits anonymous calls; CI provides a token.
    private static readonly string[] TokenVariables = ["GH_API_TOKEN", "GITHUB_TOKEN", "GH_TOKEN"];

    private static Dictionary<string, string?> GetEnvironmentVariables()
    {
        var token = TokenVariables
                   .Select(Environment.GetEnvironmentVariable)
                   .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return token is null ? [] : new() { ["GH_API_TOKEN"] = token };
    }

    public record Result(string OutputPath);
    [ServiceRegistration(ServiceLifetime.Singleton)]
    public class Settings(SharedSettings sharedSettings, IConfiguration configuration)
    {
        public bool DocsEnabled { get; } = configuration.GetValue("Docs:Enabled", true);
        public FolderPath DocsDirectory => field ??= configuration.GetValue<FolderPath?>("Docs:Directory") ?? sharedSettings.RootDirectory / "docs";
        public string DocsBuildTask => field ??= configuration.GetValue<string>("Docs:Task") ?? "docs:build";
        public FolderPath DocsOutputDirectory => field ??= configuration.GetValue<FolderPath?>("Docs:Output") ?? DocsDirectory / "dist";

        /// <summary>Whether a failed docs build leaves the run's outcome alone (<c>Docs:IgnoreFailures</c>, default <see langword="true" />).</summary>
        public bool IgnoreFailures { get; } = configuration.GetValue("Docs:IgnoreFailures", true);
    }
}
