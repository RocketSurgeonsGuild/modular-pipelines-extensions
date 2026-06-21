using Microsoft.Extensions.Logging;
using File = ModularPipelines.FileSystem.File;

namespace build.library.Modules;

[DependsOn<PackSolution>]
[DependsOn<GitVersionModule>]
public partial class PublishNuGetPackagesModule(NuGetSettings nuGetSettings, ArtifactSettings artifactSettings) : Module<CommandResult>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration
                                                         .Create()
                                                         .WithSkipWhen(() => SkipDecision.Of(
                                                             !ShouldPublish(),
                                                             "Not a CI release build — skipping NuGet publish"
                                                         ))
                                                         .Build();

    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var nugetFolder = artifactSettings.ArtifactsDirectory.GetFolder("nuget");

        var apiKey = nuGetSettings.NuGetApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            context.Logger.LogWarning("NUGET_API_KEY is not set — skipping publish");
            return null;
        }

        var packages = nugetFolder.GetFiles(f => f.Extension == "nupkg").ToList();
        var symbols = nugetFolder.GetFiles(f => f.Extension == "snupkg").ToList();

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

    private static bool ShouldPublish()
    {
        if (Environment.GetEnvironmentVariable("CI") is null &&
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null)
            return false;

        // Only publish for version branches (v*.*) — same guard as Nuke
        var branch = Environment.GetEnvironmentVariable("GITHUB_REF_NAME")
                     ?? Environment.GetEnvironmentVariable("GITHUB_HEAD_REF")
                     ?? "";
        return branch.StartsWith("v", StringComparison.OrdinalIgnoreCase) && branch.Contains('.');
    }
}
