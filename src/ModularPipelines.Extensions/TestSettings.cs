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

    /// <summary>
    ///     Whether a multi-targeted test project runs one sub module per target framework (<c>dotnet test --framework</c>), each with its own
    ///     history and slot, instead of one <c>dotnet test</c> that starts every framework's test host at once. Read from
    ///     <c>TestPerFramework</c>; on by default.
    /// </summary>
    public bool PerFramework => configuration.GetValue("TestPerFramework", true);

    /// <summary>
    ///     The target frameworks to test, such as one per CI job; every framework of each project when empty. A project that targets none of
    ///     them is left out. Read from the <c>TestFrameworks</c> list.
    /// </summary>
    public IReadOnlyList<string> Frameworks => field ??= ReadList("TestFrameworks");

    /// <summary>
    ///     The target frameworks that collect coverage; every one when empty. Coverage instrumentation costs time, and one framework's
    ///     coverage is usually enough. Read from the <c>CoverageFrameworks</c> list.
    /// </summary>
    public IReadOnlyList<string> CoverageFrameworks => field ??= ReadList("CoverageFrameworks");

    /// <summary>
    ///     Glob patterns, relative to the root directory, of test projects to start first while they have no previous run, in the order
    ///     given: the longest suites, so a first run or a pull request without history does not start them last. Read from the
    ///     <c>TestOrder</c> list.
    /// </summary>
    public IReadOnlyList<string> Order => field ??= ReadList("TestOrder");

    /// <summary>
    ///     Which part of the test run this run takes, to spread it over CI jobs: <c>TestShard: 1/2</c> and <c>2/2</c>. Each test sub module
    ///     (a project, or a project and framework) goes to a shard by its place in path order, so every job picks the same split whatever
    ///     history it has.
    /// </summary>
    public TestShard? Shard => TestShard.TryParse(configuration.GetValue<string?>("TestShard"), out var shard) ? shard : null;

    private IReadOnlyList<string> ReadList(string key) =>
        [.. configuration.GetSection(key).GetChildren().Select(z => z.Value).OfType<string>().Where(z => z.Length > 0)];

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

/// <summary>One of <see cref="Count" /> parts of the test run.</summary>
/// <param name="Index">Which part, from 1.</param>
/// <param name="Count">How many parts.</param>
public sealed record TestShard(int Index, int Count)
{
    /// <summary>Reads <c>index/count</c>, such as <c>1/2</c>.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out TestShard? shard)
    {
        shard = null;
        if (value?.Split('/', 2, StringSplitOptions.TrimEntries) is not [{ } index, { } count]
         || !int.TryParse(index, System.Globalization.CultureInfo.InvariantCulture, out var i)
         || !int.TryParse(count, System.Globalization.CultureInfo.InvariantCulture, out var n)
         || n < 1
         || i < 1
         || i > n)
        {
            return false;
        }

        shard = new(i, n);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Index}/{Count}";
}
