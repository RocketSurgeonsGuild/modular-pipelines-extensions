using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class ArtifactSettings(SharedSettings sharedSettings, IConfiguration configuration)
{
    public Folder ArtifactsDirectory => field ??= ( configuration.GetValue<Folder?>("ArtifactsDirectory") ?? sharedSettings.RootDirectory / "artifacts" ).EnsureExists();

    public Folder LogsDirectory => field ??= ( configuration.GetValue<Folder?>("LogsDirectory") ?? ArtifactsDirectory / "logs" ).EnsureExists();
}
