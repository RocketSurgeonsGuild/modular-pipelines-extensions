using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class ArtifactSettings(SharedSettings sharedSettings, IConfiguration configuration)
{
    public FolderPath ArtifactsDirectory => field ??= ( configuration.GetValue<FolderPath?>("ArtifactsDirectory") ?? sharedSettings.RootDirectory / "artifacts" ).EnsureExists();

    public FolderPath LogsDirectory => field ??= ( configuration.GetValue<FolderPath?>("LogsDirectory") ?? ArtifactsDirectory / "logs" ).EnsureExists();
}
