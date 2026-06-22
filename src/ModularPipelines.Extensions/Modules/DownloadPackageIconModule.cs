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
            context.Logger.LogInformation("No package icon url configured, skipping download");
            return null;
        }

        var destination = sharedSettings.PackageIconFile;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        context.Logger.LogInformation("Downloading package icon from {Url} to {Path}", url, destination);

        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(url, cancellationToken);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);

        context.Logger.LogInformation("Package icon staged at {Path}", destination);
        return new(destination);
    }


    public record Result(string Path);
}
