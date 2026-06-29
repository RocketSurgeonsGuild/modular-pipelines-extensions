using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.GitHub;
using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[DependsOn<PackSolution>]
[DependsOn<GitVersionModule>]
public partial class PublishNuGetPackagesModule(PublishNuGetPackagesModule.Settings nuGetSettings, ArtifactSettings artifactSettings, IGitHub github) : Module<CommandResult>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration
                                                         .Create()
                                                         .WithSkipWhen(ctx => SkipDecision.Of(
                                                             !ShouldPublish(ctx) || string.IsNullOrWhiteSpace(nuGetSettings.NuGetApiKey),
                                                             string.IsNullOrWhiteSpace(nuGetSettings.NuGetApiKey)
                                                                 ? "NUGET_API_KEY is not set — skipping NuGet publish"
                                                                 : "Ref is not a version branch/tag (v*.*) — skipping NuGet publish"
                                                         ))
                                                         .Build();

    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var nugetFolder = artifactSettings.ArtifactsDirectory.GetFolder("nuget");
        var apiKey = nuGetSettings.NuGetApiKey;

        var packages = nugetFolder.GetFiles(f => f.Extension is "nupkg" or ".nupkg").ToList();
        var symbols = nugetFolder.GetFiles(f => f.Extension is "snupkg" or ".snupkg").ToList();

        CommandResult? last = null;
        foreach (var package in packages)
            last = await PushAsync(context, package, "https://api.nuget.org/v3/index.json", apiKey, cancellationToken);

        foreach (var symbol in symbols)
            last = await PushAsync(context, symbol, "https://api.nuget.org/v3/index.json", apiKey, cancellationToken);

        return last;
    }

    private static Task<CommandResult> PushAsync(
        IModuleContext context,
        File package,
        string source,
        string apiKey,
        CancellationToken cancellationToken
    ) => context.DotNet().Nuget.Push(
        new DotNetNugetPushOptions
        {
            Path = package.Path,
            Source = source,
            ApiKey = apiKey,
            SkipDuplicate = true,
        },
        new(),
        cancellationToken
    );

    private bool ShouldPublish(IModuleContext context)
    {
        if (github.EnvironmentVariables.Actions is null)
            return false;

        if (github.EnvironmentVariables.EventName is "pull_request_target" or "merge_group" or "pull_request")
            return false;

        // Only publish for version branches (v*.*) — same guard as Nuke
        var branch = github.EnvironmentVariables.RefName ?? github.EnvironmentVariables.HeadRef ?? "";
        return branch.StartsWith("v", comparisonType: StringComparison.OrdinalIgnoreCase) && branch.Contains('.');
    }

    [ServiceRegistration(ServiceLifetime.Singleton)]
    public class Settings(IConfiguration configuration)
    {
        public string? NuGetApiKey => configuration.GetValue<string?>("RSG_NUGET_API_KEY")
                                      ?? configuration.GetValue<string?>("NUGET_API_KEY")
                                      ?? Environment.GetEnvironmentVariable("RSG_NUGET_API_KEY")
                                      ?? Environment.GetEnvironmentVariable("NUGET_API_KEY");
    }
}
