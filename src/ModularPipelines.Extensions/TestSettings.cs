using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class TestSettings(SharedSettings sharedSettings, ArtifactSettings artifactSettings, IConfiguration configuration)
{
    public bool IsEnabled => configuration.GetValue<bool?>("EnableTests") ?? true;
    public FolderPath TestArtifactsDirectory => field ??= ( sharedSettings.GetConfigurationFolder(nameof(TestArtifactsDirectory)) ?? artifactSettings.LogsDirectory / "tests" ).EnsureExists();
    public FolderPath CoverageDirectory => field ??= ( sharedSettings.GetConfigurationFolder(nameof(CoverageDirectory)) ?? artifactSettings.LogsDirectory / "coverage" ).EnsureExists();
    /// <summary>
    ///     The coverage settings passed to <c>dotnet test</c>: the configured file, or the library's default, which
    ///     <see cref="TestSolutionModule" /> writes into <see cref="TestArtifactsDirectory" /> on every run.
    /// </summary>
    public FilePath RunSettings => field ??= sharedSettings.GetConfigurationFile(nameof(RunSettings)) ?? sharedSettings.TempDirectory.GetFile("coverage.runsettings");

    /// <summary>How <see cref="TestSolutionModule" /> runs the tests. Read from <c>TestMode</c>; <see cref="TestMode.PerProject" /> by default.</summary>
    public TestMode Mode => configuration.GetValue(nameof(TestMode), TestMode.PerProject);

    /// <summary>
    ///     How many test projects run at once in <see cref="TestMode.PerProject" /> mode. Read from <c>TestParallelism</c>; half the
    ///     processors by default, since each test application runs its own tests in parallel too.
    /// </summary>
    public int Parallelism => Math.Max(1, configuration.GetValue<int?>("TestParallelism") ?? Environment.ProcessorCount / 2);
}

/// <summary>How <see cref="TestSolutionModule" /> runs the tests.</summary>
public enum TestMode
{
    /// <summary>One <c>dotnet test</c> over the solution (or a filter of it without <see cref="TestSettings.ExcludeTests" />).</summary>
    Solution,

    /// <summary>
    ///     One <c>dotnet test</c> per test project, each a sub module with its own results folder, several at once, longest first by the
    ///     durations of their last runs.
    /// </summary>
    PerProject,
}
