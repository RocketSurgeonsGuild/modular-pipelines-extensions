using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;
using ModularPipelines.Git;
using Rocket.Surgery.DependencyInjection;
using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class SharedSettings(IServiceProvider serviceProvider, IConfiguration configuration)
{
    public Folder RootDirectory => field ??= GetConfigurationFolder(nameof(RootDirectory)) ?? serviceProvider.GetRequiredService<IGit>().RootDirectory;

    public Folder TempDirectory => ( field ??= GetConfigurationFolder(nameof(TempDirectory)) ?? RootDirectory / ".temp" ).EnsureExists();

    public Uri? PackageIconUrl => field ??= configuration.GetValue<Uri?>(nameof(PackageIconUrl), null);
    public File PackageIconFile => field ??= TempDirectory.GetFile("packageicon.png");

    public Folder? GetConfigurationFolder(string key)
    {
        return configuration.GetValue<string?>(key) is string path
            ? Path.IsPathFullyQualified(path) ? new(path) : new Folder(Path.Combine(RootDirectory.Path, path))
            : null;
    }

    public File? GetConfigurationFile(string key)
    {
        return configuration.GetValue<string?>(key) is string path
            ? Path.IsPathFullyQualified(path) ? new(path) : new File(Path.Combine(RootDirectory.Path, path))
            : null;
    }
}
