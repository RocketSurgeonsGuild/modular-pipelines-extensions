using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModularPipelines.GitHub;
using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[DependsOn<PackSolution>]
[DependsOn<GitVersionModule>]
public partial class PublishNuGetPackagesModule(NuGetSettings nuGetSettings, ArtifactSettings artifactSettings, IGitHub github) : Module<CommandResult>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration
                                                         .Create()
                                                         .WithSkipWhen(ctx => SkipDecision.Of(
                                                             !ShouldPublish(ctx),
                                                             "Not a CI release build — skipping NuGet publish"
                                                         ))
                                                         .WithSkipWhen(ctx => SkipDecision.Of(
                                                             string.IsNullOrWhiteSpace(nuGetSettings.NuGetApiKey),
                                                             "NUGET_API_KEY is not set — skipping NuGet publish"
                                                         ))
                                                         .Build();

    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var nugetFolder = artifactSettings.ArtifactsDirectory.GetFolder("nuget");
        var apiKey = nuGetSettings.NuGetApiKey;

        var packages = nugetFolder.GetFiles(f => f.Extension is "nupkg" or ".nupkg").ToList();
        var symbols = nugetFolder.GetFiles(f => f.Extension is "snupkg" or ".snupkg").ToList();

        context.Logger.LogInformation("GitHub event name: {Data}", JsonSerializer.Serialize(github.EnvironmentVariables));
        return null;


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

        context.Logger.LogInformation("GitHub event name: {Data}", JsonSerializer.Serialize(github.EnvironmentVariables));
        return true;

        // Only publish for version branches (v*.*) — same guard as Nuke
        var branch = github.EnvironmentVariables.RefName ?? github.EnvironmentVariables.HeadRef ?? "";
        return branch.StartsWith("v", StringComparison.OrdinalIgnoreCase) && branch.Contains('.');
    }
}
