using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    public IReadOnlyList<string> ExcludeTests => field ??= GetList(nameof(ExcludeTests));

    /// <summary>
    ///     Glob patterns, relative to the root directory, of the only projects whose tests run, for a CI job that runs a slice of the
    ///     tests. Empty runs every test project. <see cref="ExcludeTests" /> still applies. Read from the <c>IncludeTests</c> list.
    /// </summary>
    public IReadOnlyList<string> IncludeTests => field ??= GetList(nameof(IncludeTests));

    /// <summary>The target framework the tests run on (<c>dotnet test --framework</c>), or every one. Read from <c>TestFramework</c>.</summary>
    public string? TestFramework => configuration.GetValue<string?>(nameof(TestFramework)) is { Length: > 0 } framework ? framework : null;

    /// <summary>How <see cref="Modules.TestSolution" /> runs the tests. Read from <c>TestMode</c>; <see cref="TestMode.PerProject" /> by default.</summary>
    public TestMode Mode => configuration.GetValue(nameof(TestMode), TestMode.PerProject);

    /// <summary>
    ///     How many test projects run at once in <see cref="TestMode.PerProject" /> mode. Read from <c>TestParallelism</c>; half the
    ///     processors by default, since each test run is itself parallel.
    /// </summary>
    public int Parallelism => Math.Max(1, configuration.GetValue<int?>("TestParallelism") ?? Environment.ProcessorCount / 2);

    /// <summary>
    ///     Extra module path regular expressions the default coverage settings include, on top of the assemblies of the solution's
    ///     non-test projects. Read from the <c>CoverageInclude</c> list.
    /// </summary>
    public IReadOnlyList<string> CoverageInclude => field ??= GetList(nameof(CoverageInclude));

    /// <summary>
    ///     Extra module path regular expressions the default coverage settings exclude, on top of test assemblies. Read from the
    ///     <c>CoverageExclude</c> list.
    /// </summary>
    public IReadOnlyList<string> CoverageExclude => field ??= GetList(nameof(CoverageExclude));

    /// <summary>
    ///     Extra glob patterns, relative to the root directory, of cobertura files <see cref="Modules.GenerateCodeCoverageReports" /> adds
    ///     to those under <see cref="TestsDirectory" />, such as coverage downloaded from other CI jobs. Read from the
    ///     <c>CoverageReports</c> list.
    /// </summary>
    public IReadOnlyList<string> CoverageReports => field ??= GetList(nameof(CoverageReports));

    private string[] GetList(string key) =>
        [.. configuration.GetSection(key).GetChildren().Select(z => z.Value).OfType<string>().Where(z => z.Length > 0)];
}

/// <summary>How <see cref="Modules.TestSolution" /> runs the tests.</summary>
public enum TestMode
{
    /// <summary>One <c>dotnet test</c> run over the solution (or a filter of it).</summary>
    Solution,

    /// <summary>
    ///     One <c>dotnet test</c> run per test project, each a sub module, several at once. Each project's results and coverage go to a
    ///     folder of its own, and each project's duration is kept for the next run's estimates.
    /// </summary>
    PerProject,
}
