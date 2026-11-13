using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
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

    /// <summary>
    ///     Glob patterns, relative to the root directory, of the test projects in the solution that <see cref="TestSolutionModule" /> leaves
    ///     out, such as slow suites a CI workflow runs in a job of its own. The solution still builds and packs them. Read from the
    ///     <c>ExcludeTests</c> list.
    /// </summary>
    public IReadOnlyList<string> ExcludeTests =>
        field ??= [.. configuration.GetSection(nameof(ExcludeTests)).GetChildren().Select(z => z.Value).OfType<string>().Where(z => z.Length > 0)];

    /// <summary>Whether one of <see cref="ExcludeTests" /> matches the project, by its path relative to the root directory.</summary>
    public bool IsExcluded(FilePath project) => ExcludeTests.Count > 0 && ExcludeTests.Any(pattern => Matches(pattern, GetRootRelativePath(project)));

    /// <summary>The project's path relative to the root directory, with forward slashes, as <see cref="ExcludeTests" /> patterns match it.</summary>
    public string GetRootRelativePath(FilePath project) => Path.GetRelativePath(sharedSettings.RootDirectory.Path, project.Path).Replace('\\', '/');

    internal static bool Matches(string pattern, string path)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern);
        return matcher.Match(path).HasMatches;
    }

    /// <summary>How <see cref="TestSolutionModule" /> runs the tests. Read from <c>TestMode</c>; <see cref="TestMode.PerProject" /> by default.</summary>
    public TestMode Mode => configuration.GetValue(nameof(TestMode), TestMode.PerProject);

    /// <summary>
    ///     How many test projects run at once in <see cref="TestMode.PerProject" /> mode. Read from <c>TestParallelism</c>; the processor
    ///     count by default. Test applications spend much of their time starting up, discovering tests and writing coverage, so on a
    ///     4-core CI runner four at once finished sooner than two.
    /// </summary>
    public int Parallelism => Math.Max(1, configuration.GetValue<int?>("TestParallelism") ?? Environment.ProcessorCount);
}

/// <summary>How <see cref="TestSolutionModule" /> runs the tests.</summary>
public enum TestMode
{
    /// <summary>One <c>dotnet test</c> over the solution.</summary>
    Solution,

    /// <summary>
    ///     One <c>dotnet test</c> per test project, each a sub module with its own results folder, several at once, longest first by the
    ///     durations of their last runs.
    /// </summary>
    PerProject,
}
