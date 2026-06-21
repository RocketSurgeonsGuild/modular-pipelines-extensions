using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;
using Rocket.Surgery.DependencyInjection;

namespace build.library;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class ArtifactSettings(SharedSettings sharedSettings, IConfiguration configuration)
{
    public Folder ArtifactsDirectory => field ??= configuration.GetValue<Folder?>("ArtifactsDirectory") ?? sharedSettings.RootDirectory / "artifacts";

    public Folder LogsDirectory => field ??= configuration.GetValue<Folder?>("LogsDirectory") ?? ArtifactsDirectory / "logs";
}
