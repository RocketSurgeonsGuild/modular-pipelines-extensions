using Microsoft.Extensions.Logging;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

public partial class DownloadPackageIconModule(SharedSettings sharedSettings) : Module<DownloadPackageIconModule.Result>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration
                                                         .Create()
                                                         .WithSkipWhen(() => SkipDecision.Of(
                                                             sharedSettings.PackageIconFile.Exists,
                                                             "Package icon already staged"
                                                         ))
                                                         .Build();

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

        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(url, cancellationToken);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);

        LogPackageIconStaged(context.Logger, destination);
        return new(destination);
    }

    [LoggerMessage(LogLevel.Information, Message = "No package icon url configured, skipping download")]
    private static partial void NoPackageIconConfigured(ILogger logger);

    [LoggerMessage(LogLevel.Information, Message = "Downloading package icon from {Url} to {Path}")]
    private static partial void LogDownloadingPackageIcon(ILogger logger, Uri url, string path);

    [LoggerMessage(LogLevel.Information, Message = "Package icon staged at {Path}")]
    private static partial void LogPackageIconStaged(ILogger logger, string path);

    public record Result(string Path);
}
