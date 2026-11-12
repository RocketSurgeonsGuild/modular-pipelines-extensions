using Microsoft.Extensions.Logging;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[DependsOn<GitVersionModule>]
public partial class ShipPublicApisModule(SharedSettings sharedSettings) : Module<ShipPublicApisModule.Result?>
{
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(!ShouldShip(),
            "Not a release build — skipping public API promotion")
    );

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var gitVersion = await context.GetModule<GitVersionModule>();
        if (gitVersion.ValueOrDefault?.IsRelease != true)
        {
            context.Logger.LogInformation("Prerelease — skipping public API ship");
            return null;
        }

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

            LogShippedApis(context.Logger, newEntries.Count, unshipped.Path);
            shipped++;
        }

        return new(shipped, unshippedFiles.Count);
    }

    private static bool ShouldShip() =>
        Environment.GetEnvironmentVariable("CI") is not null ||
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null;

    public record Result(int FilesShipped, int FilesFound);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Shipped {Count} APIs from {File}")]
    private static partial void LogShippedApis(ILogger logger, int count, string file);
}
