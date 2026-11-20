using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rocket.Surgery.ModularPipelines.Extensions;

/// <summary>Where the pipeline writes its artifacts and logs.</summary>
/// <param name="sharedSettings">The settings shared by every module.</param>
/// <param name="configuration">The pipeline configuration.</param>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class ArtifactSettings(SharedSettings sharedSettings, IConfiguration configuration)
{
    /// <summary>The artifacts folder, created if missing. Read from <c>ArtifactsDirectory</c>; <c>artifacts</c> under the root directory by default.</summary>
    public FolderPath ArtifactsDirectory => field ??= ( configuration.GetValue<FolderPath?>("ArtifactsDirectory") ?? sharedSettings.RootDirectory / "artifacts" ).EnsureExists();

    /// <summary>The logs folder, created if missing. Read from <c>LogsDirectory</c>; <c>logs</c> under <see cref="ArtifactsDirectory" /> by default.</summary>
    public FolderPath LogsDirectory => field ??= ( configuration.GetValue<FolderPath?>("LogsDirectory") ?? ArtifactsDirectory / "logs" ).EnsureExists();
}
