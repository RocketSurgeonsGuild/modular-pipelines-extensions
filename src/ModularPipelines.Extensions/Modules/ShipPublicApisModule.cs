using Microsoft.Extensions.Logging;

namespace build.library.Modules;

[DependsOn<GitVersionModule>]
public partial class ShipPublicApisModule(SharedSettings sharedSettings, GitVersionModule gitVersionModule) : Module<ShipPublicApisModule.Result>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration
                                                         .Create()
                                                         .WithSkipWhen(() => SkipDecision.Of(
                                                             !ShouldShip(),
                                                             "Not a release build — skipping public API promotion"
                                                         ))
                                                         .Build();

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var gitVersion = await gitVersionModule;

        var srcDir = sharedSettings.RootDirectory.GetFolder("src");
        var unshippedFiles = srcDir
                            .GetFiles(f => f.Name == "PublicAPI.Unshipped.txt")
                            .ToList();

        var shipped = 0;
        foreach (var unshipped in unshippedFiles)
        {
            var shippedPath = Path.Combine(unshipped.Folder!.Path, "PublicAPI.Shipped.txt");
            var unshippedLines = await File.ReadAllLinesAsync(unshipped.Path, cancellationToken);
            var newEntries = unshippedLines
                            .Where(l => !string.IsNullOrWhiteSpace(l) && l != "#nullable enable")
                            .ToList();

            if (newEntries.Count == 0) continue;

            var existingShipped = File.Exists(shippedPath)
                ? await File.ReadAllLinesAsync(shippedPath, cancellationToken)
                : ["#nullable enable"];

            var allShipped = existingShipped
                            .Concat(newEntries)
                            .Distinct()
                            .OrderBy(l => l == "#nullable enable" ? "" : l)
                            .ToList();

            await File.WriteAllLinesAsync(shippedPath, allShipped, cancellationToken);
            await File.WriteAllLinesAsync(unshipped.Path, ["#nullable enable"], cancellationToken);

            context.Logger.LogInformation("Shipped {Count} APIs from {File}", newEntries.Count, unshipped.Path);
            shipped++;
        }

        return new(shipped, unshippedFiles.Count);
    }

    private static bool ShouldShip() =>
        Environment.GetEnvironmentVariable("CI") is not null ||
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null;

    public record Result(int FilesShipped, int FilesFound);
}
