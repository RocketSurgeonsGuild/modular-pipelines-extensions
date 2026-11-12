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
    /// <summary>
    ///     The coverage settings passed to <c>dotnet test</c>: the configured file, or the library's default, which
    ///     <see cref="Modules.TestSolution" /> writes into <see cref="TestsDirectory" /> on every run.
    /// </summary>
    public File RunSettings => field ??= sharedSettings.GetConfigurationFile(nameof(RunSettings)) ?? TestsDirectory.GetFile("coverage.runsettings");

    /// <summary>Whether <see cref="RunSettings" /> is the library's default rather than a configured file.</summary>
    public bool UsesDefaultRunSettings => sharedSettings.GetConfigurationFile(nameof(RunSettings)) is null;
}
