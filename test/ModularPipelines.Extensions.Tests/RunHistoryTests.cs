using Joinery.Dovetail.Joints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModularPipelines.Caching;
using ModularPipelines.Options;
using ModularPipelines.Reporting;
using Rocket.Surgery.ModularPipelines.Extensions.History;
using Rocket.Surgery.ModularPipelines.Extensions.Joints;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

namespace Rocket.Surgery.ModularPipelines.Extensions.Tests;

public class RunHistoryTests
{
    [Test]
    public async Task Keeps_run_history_under_the_root_directory_with_a_stable_identity()
    {
        var root = Directory.CreateTempSubdirectory("rocket-history-");
        try
        {
            await using var pipeline = await CreateBuilder(("RootDirectory", root.FullName), ("History:Identity", "rocket/build")).ConfigureDovetail();

            var runReport = pipeline.Services.GetRequiredService<IOptions<PipelineOptions>>().Value.RunReport;
            await Assert.That(runReport.HistoryDirectory).IsEqualTo(Path.Combine(root.FullName, ".modularpipelines", "run-history"));
            await Assert.That(runReport.HistoryRetention).IsEqualTo(30);
            await Assert.That(runReport.PipelineIdentity).IsEqualTo("rocket/build");
            await Assert.That(pipeline.Services.GetServices<IModuleEstimatedTimeProvider>().OfType<RunHistoryEstimatedTimeProvider>()).IsNotEmpty();
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public async Task Turns_run_history_off_and_keeps_the_estimates_when_disabled()
    {
        await using var pipeline = await CreateBuilder(("History:Enabled", "false")).ConfigureDovetail();

        var runReport = pipeline.Services.GetRequiredService<IOptions<PipelineOptions>>().Value.RunReport;
        await Assert.That(runReport.HistoryRetention).IsEqualTo(0);
        await Assert.That(pipeline.Services.GetServices<IModuleEstimatedTimeProvider>().OfType<RunHistoryEstimatedTimeProvider>()).IsNotEmpty();
    }

    [Test]
    public async Task Keeps_the_module_cache_under_the_root_directory()
    {
        var root = Directory.CreateTempSubdirectory("rocket-cache-");
        try
        {
            await using var pipeline = await CreateBuilder(("RootDirectory", root.FullName)).ConfigureDovetail();

            var cache = pipeline.Services.GetRequiredService<IOptions<ModuleCacheOptions>>().Value;
            await Assert.That(pipeline.Services.GetService<IModuleCacheStore>()).IsTypeOf<FileSystemModuleCache>();
            await Assert.That(cache.WorkingDirectory).IsEqualTo(root.FullName);
            await Assert.That(cache.CacheDirectory).IsEqualTo(Path.Combine(root.FullName, ".modularpipelines", "module-cache"));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public async Task Leaves_the_module_cache_out_when_disabled()
    {
        await using var pipeline = await CreateBuilder(("History:ModuleCache:Enabled", "false")).ConfigureDovetail();

        await Assert.That(pipeline.Services.GetService<IModuleCacheStore>()).IsNull();
    }

    [Test]
    public async Task Restores_a_cacheable_module_and_keeps_everything_under_modularpipelines()
    {
        var root = Directory.CreateTempSubdirectory("rocket-cache-");
        try
        {
            CachedModule.Executions = 0;

            var first = await RunCachedModuleAsync(root.FullName);
            var second = await RunCachedModuleAsync(root.FullName);

            await Assert.That(first.GetResult<CachedModule, string>().Status).IsEqualTo(ModuleStatus.Succeeded);
            await Assert.That(second.GetResult<CachedModule, string>().Status).IsEqualTo(ModuleStatus.RestoredFromCache);
            await Assert.That(second.GetResult<CachedModule, string>().ValueOrDefault).IsEqualTo("cached");
            await Assert.That(CachedModule.Executions).IsEqualTo(1);
            await Assert.That(root.GetDirectories().Select(z => z.Name)).IsEquivalentTo([".modularpipelines"]);
            await Assert.That(new DirectoryInfo(Path.Combine(root.FullName, ".modularpipelines")).GetDirectories().Select(z => z.Name))
                       .IsEquivalentTo(["estimates", "module-cache", "run-history"]);
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static async Task<PipelineSummary> RunCachedModuleAsync(string root)
    {
        await using var pipeline = await CreateBuilder(("RootDirectory", root), ("History:Identity", "cache-test"))
                                       .ConfigureDovetail(
                                            z => z
                                                .ExceptJoint(typeof(ModulesJoint))
                                                .ConfigurePipelineBuilder((_, builder) => builder.AddModule<CachedModule>())
                                        );
        return await pipeline.RunAsync();
    }

    [Test]
    public async Task Leaves_run_history_to_modular_pipelines_without_the_joint()
    {
        await using var pipeline = await Pipeline.CreateBuilder().ConfigureDovetail(z => z.ExceptJoint(typeof(RunHistoryJoint)));

        await Assert.That(pipeline.Services.GetServices<IModuleEstimatedTimeProvider>().OfType<RunHistoryEstimatedTimeProvider>()).IsEmpty();
    }

    [Test]
    public async Task Reads_the_options_from_the_history_section()
    {
        var root = Path.Combine(Path.GetTempPath(), "rocket-root");
        var configuration = new ConfigurationBuilder()
                           .AddInMemoryCollection(
                                [
                                    new("RootDirectory", root),
                                    new("History:Directory", "history"),
                                    new("History:EstimatesDirectory", "/var/estimates"),
                                    new("History:Retention", "5"),
                                    new("History:Samples", "3"),
                                    new("History:Identity", "custom"),
                                    new("History:ModuleCache:Enabled", "true"),
                                ]
                            )
                           .Build();

        var options = RocketHistoryOptions.FromConfiguration(configuration, Path.GetTempPath());

        await Assert.That(options.Enabled).IsTrue();
        await Assert.That(options.RootDirectory).IsEqualTo(Path.GetFullPath(root));
        await Assert.That(options.Resolve(options.HistoryDirectory)).IsEqualTo(Path.Combine(Path.GetFullPath(root), "history"));
        await Assert.That(options.Resolve(options.EstimatesDirectory)).IsEqualTo(Path.GetFullPath("/var/estimates"));
        await Assert.That(options.Retention).IsEqualTo(5);
        await Assert.That(options.EstimateSamples).IsEqualTo(3);
        await Assert.That(options.PipelineIdentity).IsEqualTo("custom");
        await Assert.That(options.ModuleCache).IsTrue();
    }

    [Test]
    public async Task Estimates_a_module_from_the_median_of_its_successful_runs()
    {
        var root = Directory.CreateTempSubdirectory("rocket-estimates-");
        try
        {
            var reader = new FakeHistoryReader(
                Sample(ModuleStatus.Succeeded, 10),
                Sample(ModuleStatus.Failed, 1),
                Sample(ModuleStatus.Succeeded, 30),
                Sample(ModuleStatus.Succeeded, 20)
            );
            var provider = new RunHistoryEstimatedTimeProvider(reader, Options(root.FullName), NullLogger<RunHistoryEstimatedTimeProvider>.Instance);

            var estimate = await provider.GetModuleEstimatedTimeAsync(ModuleId.FromType(typeof(BuildSolution)));

            await Assert.That(estimate).IsEqualTo(TimeSpan.FromSeconds(20));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public async Task Falls_back_to_the_last_saved_duration_without_successful_runs()
    {
        var root = Directory.CreateTempSubdirectory("rocket-estimates-");
        try
        {
            var provider = new RunHistoryEstimatedTimeProvider(
                new FakeHistoryReader(Sample(ModuleStatus.Failed, 1)),
                Options(root.FullName),
                NullLogger<RunHistoryEstimatedTimeProvider>.Instance
            );
            var moduleId = ModuleId.FromType(typeof(BuildSolution));

            var before = await provider.GetModuleEstimatedTimeAsync(moduleId);
            await provider.SaveModuleTimeAsync(moduleId, TimeSpan.FromSeconds(42));
            var after = await provider.GetModuleEstimatedTimeAsync(moduleId);

            await Assert.That(before).IsEqualTo(TimeSpan.FromMinutes(2));
            await Assert.That(after).IsEqualTo(TimeSpan.FromSeconds(42));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    public async Task Estimates_each_sub_module_from_the_median_of_its_last_durations()
    {
        var root = Directory.CreateTempSubdirectory("rocket-estimates-");
        try
        {
            var provider = new RunHistoryEstimatedTimeProvider(
                new FakeHistoryReader(),
                Options(root.FullName) with { EstimateSamples = 3 },
                NullLogger<RunHistoryEstimatedTimeProvider>.Instance
            );
            var moduleId = ModuleId.FromType(typeof(TestSolution));

            // Saved side by side, as sub modules finish.
            await Task.WhenAll(
                provider.SaveSubModuleTimeAsync(moduleId, new("a", TimeSpan.FromSeconds(100))),
                provider.SaveSubModuleTimeAsync(moduleId, new("b", TimeSpan.FromSeconds(2)))
            );
            // a keeps its last three: 9, 3 and 5, so the 100 has dropped out.
            foreach (var seconds in (int[])[9, 3, 5])
            {
                await provider.SaveSubModuleTimeAsync(moduleId, new("a", TimeSpan.FromSeconds(seconds)));
            }

            var estimates = ( await provider.GetSubModuleEstimatedTimesAsync(moduleId) ).ToDictionary(z => z.SubModuleName, z => z.EstimatedDuration);

            await Assert.That(estimates).IsEquivalentTo(new Dictionary<string, TimeSpan> { ["a"] = TimeSpan.FromSeconds(5), ["b"] = TimeSpan.FromSeconds(2) });
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static PipelineBuilder CreateBuilder(params (string Key, string Value)[] settings)
    {
        var builder = Pipeline.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings.Select(z => new KeyValuePair<string, string?>(z.Key, z.Value)));
        return builder;
    }

    private static RocketHistoryOptions Options(string root) => new() { RootDirectory = root };

    private static ModuleDurationSample Sample(ModuleStatus status, int seconds) =>
        new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, status, TimeSpan.FromSeconds(seconds));

    // Opts into the module cache with a key part; ModularPipelines caches only the modules that declare what their result depends on.
    public sealed class CachedModule : Module<string>
    {
        public static int Executions;

        protected override void Configure(ModuleConfigurationBuilder module) => module.WithCacheKeyPart("version=1");

        protected override Task<string> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Executions);
            return Task.FromResult("cached");
        }
    }

    private sealed class FakeHistoryReader(params ModuleDurationSample[] samples) : IRunHistoryReader
    {
        public Task<IReadOnlyList<ModuleDurationSample>> GetModuleDurationTrendAsync(ModuleId moduleId, int lastN, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModuleDurationSample>>([.. samples.Take(lastN)]);
    }
}
