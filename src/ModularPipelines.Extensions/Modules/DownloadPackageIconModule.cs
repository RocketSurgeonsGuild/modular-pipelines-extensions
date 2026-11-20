using Microsoft.Extensions.Logging;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Downloads the package icon from <see cref="SharedSettings.PackageIconUrl" /> to <see cref="SharedSettings.PackageIconFile" />, which
///     the packages include. Skipped when the icon is already there.
/// </summary>
/// <param name="sharedSettings">Where the icon comes from and goes.</param>
public partial class DownloadPackageIconModule(SharedSettings sharedSettings) : Module<DownloadPackageIconModule.Result?>
{
    /// <inheritdoc />
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(sharedSettings.PackageIconFile.Exists,
            "Package icon already staged")
    );

    /// <inheritdoc />
    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var url = sharedSettings.PackageIconUrl;
        if (url is null)
        {
            NoPackageIconConfigured(context.Logger);
            return null;
        }

        var destination = sharedSettings.PackageIconFile;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        LogDownloadingPackageIcon(context.Logger, url, destination);

        await context.Network.Downloader.DownloadFileAsync(new(url) { SavePath = destination, Overwrite = true }, cancellationToken);

        LogPackageIconStaged(context.Logger, destination);
        return new(destination);
    }

    [LoggerMessage(LogLevel.Information, Message = "No package icon url configured, skipping download")]
    private static partial void NoPackageIconConfigured(ILogger logger);

    [LoggerMessage(LogLevel.Information, Message = "Downloading package icon from {Url} to {Path}")]
    private static partial void LogDownloadingPackageIcon(ILogger logger, Uri url, string path);

    [LoggerMessage(LogLevel.Information, Message = "Package icon staged at {Path}")]
    private static partial void LogPackageIconStaged(ILogger logger, string path);

    /// <summary>The downloaded icon.</summary>
    /// <param name="Path">Where the icon was saved.</param>
    public record Result(string Path);
}
