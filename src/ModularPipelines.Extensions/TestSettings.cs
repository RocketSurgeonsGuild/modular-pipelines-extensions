using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;
using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class TestSettings(SharedSettings sharedSettings, ArtifactSettings artifactSettings, IConfiguration configuration)
{
    public bool IsEnabled => configuration.GetValue<bool?>("EnableTests") ?? true;
    public Folder TestsDirectory => field ??= ( sharedSettings.GetConfigurationFolder(nameof(TestsDirectory)) ?? artifactSettings.LogsDirectory / "tests" ).EnsureExists();
    public Folder CoverageDirectory => field ??= ( sharedSettings.GetConfigurationFolder(nameof(CoverageDirectory)) ?? artifactSettings.LogsDirectory / "coverage" ).EnsureExists();
    public File RunSettings => field ??= sharedSettings.GetConfigurationFile(nameof(RunSettings)) ?? TestsDirectory + "coverage.runsettings";
}
