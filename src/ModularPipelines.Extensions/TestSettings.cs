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
    public File RunSettings => field ??= sharedSettings.GetConfigurationFile(nameof(RunSettings)) ?? ( TestsDirectory + "coverage.runsettings" )!;

    /// <summary>
    ///     Glob patterns, relative to the root directory, of the projects in the solution that <see cref="Modules.TestSolution" /> leaves
    ///     out, such as slow suites a CI workflow runs in a job of its own. The solution still builds and packs them. Read from the
    ///     <c>ExcludeTests</c> list.
    /// </summary>
    public IReadOnlyList<string> ExcludeTests =>
        field ??= [.. configuration.GetSection(nameof(ExcludeTests)).GetChildren().Select(z => z.Value).OfType<string>().Where(z => z.Length > 0)];
}
