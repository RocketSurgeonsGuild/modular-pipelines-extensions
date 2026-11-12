using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class TestSettings(SharedSettings sharedSettings, ArtifactSettings artifactSettings, IConfiguration configuration)
{
    public bool IsEnabled => configuration.GetValue<bool?>("EnableTests") ?? true;
    public FolderPath TestsDirectory => field ??= ( sharedSettings.GetConfigurationFolder(nameof(TestsDirectory)) ?? artifactSettings.LogsDirectory / "tests" ).EnsureExists();
    public FolderPath CoverageDirectory => field ??= ( sharedSettings.GetConfigurationFolder(nameof(CoverageDirectory)) ?? artifactSettings.LogsDirectory / "coverage" ).EnsureExists();
    /// <summary>
    ///     The coverage settings passed to <c>dotnet test</c>: the configured file, or the library's default, which
    ///     <see cref="Modules.TestSolution" /> writes into <see cref="TestsDirectory" /> on every run.
    /// </summary>
    public FilePath RunSettings => field ??= sharedSettings.GetConfigurationFile(nameof(RunSettings)) ?? TestsDirectory.GetFile("coverage.runsettings");

    /// <summary>Whether <see cref="RunSettings" /> is the library's default rather than a configured file.</summary>
    public bool UsesDefaultRunSettings => sharedSettings.GetConfigurationFile(nameof(RunSettings)) is null;

    /// <summary>
    ///     Glob patterns, relative to the root directory, of the projects in the solution that <see cref="Modules.TestSolution" /> leaves
    ///     out, such as slow suites a CI workflow runs in a job of its own. The solution still builds and packs them. Read from the
    ///     <c>ExcludeTests</c> list.
    /// </summary>
    public IReadOnlyList<string> ExcludeTests =>
        field ??= [.. configuration.GetSection(nameof(ExcludeTests)).GetChildren().Select(z => z.Value).OfType<string>().Where(z => z.Length > 0)];
}
