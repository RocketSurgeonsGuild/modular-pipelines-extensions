using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;
using Rocket.Surgery.DependencyInjection;
using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class TestSettings(SharedSettings sharedSettings, ArtifactSettings artifactSettings, IConfiguration configuration)
{
public bool IsEnabled => configuration.GetValue<bool?>("EnableTests") ?? true;
    public Folder TestsDirectory => field ??= ( sharedSettings.GetConfigurationFolder(nameof(TestsDirectory)) ?? artifactSettings.ArtifactsDirectory / "tests" ).EnsureExists();
    public Folder CoverageDirectory => field ??= (sharedSettings.GetConfigurationFolder(nameof(CoverageDirectory)) ?? artifactSettings.ArtifactsDirectory / "coverage").EnsureExists();
    public File RunSettings => field ??= sharedSettings.GetConfigurationFile(nameof(RunSettings)) ?? TestsDirectory + "coverage.runsettings";
}
