using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;
using Rocket.Surgery.DependencyInjection;
using File = ModularPipelines.FileSystem.File;

namespace build.library;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class TestSettings(SharedSettings sharedSettings, ArtifactSettings artifactSettings)
{
    public Folder TestsDirectory => field ??= sharedSettings.GetConfigurationFolder(nameof(TestsDirectory)) ?? artifactSettings.ArtifactsDirectory / "tests";
    public Folder CoverageDirectory => field ??= sharedSettings.GetConfigurationFolder(nameof(CoverageDirectory)) ?? artifactSettings.ArtifactsDirectory / "coverage";
    public File RunSettings => field ??= sharedSettings.GetConfigurationFile(nameof(RunSettings)) ?? TestsDirectory + "coverage.runsettings";
}
