using Microsoft.Extensions.Configuration;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

namespace Rocket.Surgery.ModularPipelines.Extensions.Tests;

public class TestQueueTests
{
    [Test]
    public async Task Starts_the_projects_with_no_history_and_then_the_longest()
    {
        var queue = TestQueue.Order(
            [Project("Fast"), Project("New.B"), Project("Slow"), Project("New.A"), Project("Medium")],
            Estimates(("Fast", 5), ("Slow", 60), ("Medium", 20))
        );

        await Assert.That(Describe(queue)).IsEqualTo("New.A=?, New.B=?, Slow=60, Medium=20, Fast=5");
    }

    [Test]
    public async Task Breaks_ties_by_path_so_the_same_history_gives_the_same_order()
    {
        var estimates = Estimates(("B", 10), ("A", 10), ("C", 10));

        var first = TestQueue.Order([Project("C"), Project("A"), Project("B")], estimates);
        var second = TestQueue.Order([Project("B"), Project("C"), Project("A")], estimates);

        await Assert.That(Describe(first)).IsEqualTo("A=10, B=10, C=10");
        await Assert.That(Describe(second)).IsEqualTo(Describe(first));
    }

    [Test]
    public async Task Estimates_the_run_as_each_free_slot_takes_the_next_project()
    {
        // Two slots: A runs 0-60 in the first; the fast lane runs D 0-20, C 20-45 and B 45-75.
        var queue = TestQueue.Order(
            [Project("A"), Project("B"), Project("C"), Project("D")],
            Estimates(("A", 60), ("B", 30), ("C", 25), ("D", 20))
        );

        await Assert.That(TestQueue.EstimateDuration(queue, 2, true)).IsEqualTo(TimeSpan.FromSeconds(75));
        // Without it: B frees its slot at 30 for C (30-55), and C at 55 for D (55-75).
        await Assert.That(TestQueue.EstimateDuration(queue, 2, false)).IsEqualTo(TimeSpan.FromSeconds(75));
        await Assert.That(TestQueue.EstimateDuration(queue, 1, false)).IsEqualTo(TimeSpan.FromSeconds(135));
        await Assert.That(TestQueue.EstimateDuration(queue, 8, true)).IsEqualTo(TimeSpan.FromSeconds(60));
        // As fast either way, so the short projects get their own slot.
        await Assert.That(TestQueue.UsesFastLane(queue, 2)).IsTrue();
    }

    [Test]
    public async Task The_fast_lane_takes_the_shortest_project_left_and_the_others_the_longest()
    {
        var lanes = new TestLanes(
            TestQueue.Order(
                [Project("A"), Project("B"), Project("C"), Project("D"), Project("E")],
                Estimates(("A", 60), ("B", 30), ("C", 25), ("D", 20), ("E", 5))
            )
        );

        var taken = new List<string>();
        foreach (var shortest in new[] { true, false, true, true, false, true })
        {
            taken.Add(lanes.TryTake(shortest, out var entry) ? entry.Name : "-");
        }

        await Assert.That(string.Join(", ", taken)).IsEqualTo("E, A, D, C, B, -");
    }

    [Test]
    public async Task Has_no_fast_lane_with_one_slot_or_a_slot_for_every_project()
    {
        var queue = TestQueue.Order([Project("A"), Project("B")], Estimates(("A", 60), ("B", 5)));

        await Assert.That(TestQueue.UsesFastLane(queue, 1)).IsFalse();
        // Every project starts at once.
        await Assert.That(TestQueue.UsesFastLane(queue, 2)).IsFalse();
    }

    [Test]
    public async Task Has_no_fast_lane_without_enough_history_to_compare()
    {
        // Nothing has run before: both estimates are zero, which used to pick the fast lane and start a long project late.
        var none = TestQueue.Order([Project("A"), Project("B"), Project("C"), Project("D"), Project("E")], Estimates());
        // Only two of five have run: with three slots, the estimates cannot say which order finishes first.
        var some = TestQueue.Order(
            [Project("A"), Project("B"), Project("C"), Project("D"), Project("E")],
            Estimates(("A", 60), ("E", 5))
        );

        await Assert.That(TestQueue.UsesFastLane(none, 2)).IsFalse();
        await Assert.That(TestQueue.UsesFastLane(some, 3)).IsFalse();
    }

    [Test]
    public async Task Leaves_out_the_fast_lane_when_it_would_finish_later()
    {
        // Three slots. With the fast lane, A (0-60) and B (0-50) take the longest while it runs the five 10s from 0 to 50 and then C
        // (50-80). Without it, C (0-30) is followed by the 10s, and the run ends at 70.
        var queue = TestQueue.Order(
            [Project("A"), Project("B"), Project("C"), Project("F1"), Project("F2"), Project("F3"), Project("F4"), Project("F5")],
            Estimates(("A", 60), ("B", 50), ("C", 30), ("F1", 10), ("F2", 10), ("F3", 10), ("F4", 10), ("F5", 10))
        );

        await Assert.That(TestQueue.EstimateDuration(queue, 3, true)).IsEqualTo(TimeSpan.FromSeconds(80));
        await Assert.That(TestQueue.EstimateDuration(queue, 3, false)).IsEqualTo(TimeSpan.FromSeconds(70));
        await Assert.That(TestQueue.UsesFastLane(queue, 3)).IsFalse();
    }

    [Test]
    public async Task Uses_the_fast_lane_when_the_run_finishes_as_early_with_it()
    {
        // Three slots: A and B run 0-60 in two of them, and the six 10s fill the third from 0 to 60, with or without the fast lane.
        var queue = TestQueue.Order(
            [Project("A"), Project("B"), Project("F1"), Project("F2"), Project("F3"), Project("F4"), Project("F5"), Project("F6")],
            Estimates(("A", 60), ("B", 60), ("F1", 10), ("F2", 10), ("F3", 10), ("F4", 10), ("F5", 10), ("F6", 10))
        );

        await Assert.That(TestQueue.EstimateDuration(queue, 3, true)).IsEqualTo(TimeSpan.FromSeconds(60));
        await Assert.That(TestQueue.EstimateDuration(queue, 3, false)).IsEqualTo(TimeSpan.FromSeconds(60));
        await Assert.That(TestQueue.UsesFastLane(queue, 3)).IsTrue();
    }

    [Test]
    public async Task Describes_the_order_with_each_projects_last_runs()
    {
        var queue = TestQueue.Order([Project("Slow"), Project("New"), Project("Fast")], Estimates(("Slow", 60), ("Fast", 5)));

        var description = TestQueue.Describe(queue, 2, true);

        await Assert.That(description).IsEqualTo(
            string.Join(
                Environment.NewLine,
                (string[])[
                "3 test projects, 2 at a time, longest first by their last runs, one slot taking the shortest first (estimated 1 minute for the 2 with a previous run; the others start first, in path order)",
                "  1. New: no previous run",
                "  2. Slow: 1 minute",
                "  3. Fast: 5 seconds",
                ]
            )
        );
    }

    [Test]
    public async Task Describes_path_order_without_history()
    {
        var queue = TestQueue.Order([Project("B"), Project("A")], Estimates());

        await Assert.That(TestQueue.Describe(queue, 4, false)).StartsWith("2 test projects, 4 at a time, in path order: none has a previous run");
        await Assert.That(TestQueue.Describe(queue, 4, false, true)).StartsWith("2 test projects, 4 at a time, in TestOrder first, then path order: none has a previous run");
    }

    [Test]
    public async Task Runs_tests_per_project_on_every_processor_by_default()
    {
        var configuration = new ConfigurationBuilder().Build();
        var settings = new TestSettings(null!, null!, configuration);

        await Assert.That(settings.Mode).IsEqualTo(TestMode.PerProject);
        await Assert.That(settings.Parallelism).IsEqualTo(Math.Max(1, Environment.ProcessorCount));
    }

    [Test]
    public async Task Reads_the_test_mode_and_parallelism()
    {
        var configuration = new ConfigurationBuilder()
                           .AddInMemoryCollection([new("TestMode", "Solution"), new("TestParallelism", "0")])
                           .Build();
        var settings = new TestSettings(null!, null!, configuration);

        await Assert.That(settings.Mode).IsEqualTo(TestMode.Solution);
        await Assert.That(settings.Parallelism).IsEqualTo(1);
    }

    [Test]
    public async Task Excludes_test_projects_by_their_path_from_the_root_directory()
    {
        var configuration = new ConfigurationBuilder()
                           .AddInMemoryCollection([new("RootDirectory", "/repo"), new("ExcludeTests:0", "test/Slow*/**"), new("ExcludeTests:1", "**/*.Integration.csproj")])
                           .Build();
        var settings = new TestSettings(new(configuration), null!, configuration);

        await Assert.That(settings.ExcludeTests.Count).IsEqualTo(2);
        await Assert.That(settings.IsExcluded(new("/repo/test/Slow.Tests/Slow.Tests.csproj"))).IsTrue();
        await Assert.That(settings.IsExcluded(new("/repo/test/Api/Api.Integration.csproj"))).IsTrue();
        await Assert.That(settings.IsExcluded(new("/repo/test/Fast.Tests/Fast.Tests.csproj"))).IsFalse();
    }

    [Test]
    public async Task Excludes_nothing_by_default()
    {
        var configuration = new ConfigurationBuilder().Build();
        var settings = new TestSettings(null!, null!, configuration);

        await Assert.That(settings.ExcludeTests).IsEmpty();
        await Assert.That(settings.IsExcluded(new("/repo/test/A/A.csproj"))).IsFalse();
    }

    [Test]
    public async Task Runs_each_framework_of_a_multi_targeted_project_as_its_own_sub_module()
    {
        var targets = TestQueue.Expand([Project("Multi", "net10.0", "net11.0"), Project("Single", "net11.0"), Project("Unknown")], true, []).ToArray();

        await Assert.That(Describe(targets)).IsEqualTo("Multi [net10.0], Multi [net11.0], Single, Unknown");
        // Off, a project runs as a whole.
        await Assert.That(Describe(TestQueue.Expand([Project("Multi", "net10.0", "net11.0")], false, []))).IsEqualTo("Multi");
    }

    [Test]
    public async Task Runs_only_the_frameworks_asked_for()
    {
        var targets = TestQueue.Expand(
            [Project("Multi", "net10.0", "net11.0"), Project("Old", "net10.0"), Project("New", "net11.0"), Project("Unknown")],
            false,
            ["net11.0"]
        );

        // A subset of a project's frameworks has to be passed with --framework, even with TestPerFramework off; a project with none of
        // them is left out, and one MSBuild said nothing about still runs.
        await Assert.That(Describe(targets)).IsEqualTo("Multi [net11.0], New, Unknown");
    }

    [Test]
    public async Task Splits_the_targets_into_shards_by_path_order()
    {
        TestTarget[] targets =
        [
            new(Project("C")), new(Project("A", "net10.0", "net11.0"), "net11.0"), new(Project("B")), new(Project("A", "net10.0", "net11.0"), "net10.0"),
        ];

        await Assert.That(Describe(TestQueue.InShard(targets, new(1, 2)))).IsEqualTo("A [net10.0], B");
        await Assert.That(Describe(TestQueue.InShard(targets, new(2, 2)))).IsEqualTo("A [net11.0], C");
        await Assert.That(TestQueue.InShard(targets, null).Count()).IsEqualTo(4);
    }

    [Test]
    [Arguments("1/2", 1, 2)]
    [Arguments(" 3 / 3 ", 3, 3)]
    public async Task Reads_a_shard(string value, int index, int count)
    {
        await Assert.That(TestShard.TryParse(value, out var shard)).IsTrue();
        await Assert.That(shard).IsEqualTo(new TestShard(index, count));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("2")]
    [Arguments("0/2")]
    [Arguments("3/2")]
    [Arguments("a/b")]
    public async Task Ignores_a_shard_that_is_not_index_of_count(string? value) => await Assert.That(TestShard.TryParse(value, out _)).IsFalse();

    [Test]
    public async Task Starts_the_projects_named_first_while_they_have_no_history()
    {
        var priority = new Dictionary<string, int>(StringComparer.Ordinal) { ["Long"] = 0, ["Longer"] = 0, ["Medium"] = 1 };
        var queue = TestQueue.Order(
            [new TestTarget(Project("A")), new(Project("Medium")), new(Project("Long")), new(Project("Known")), new(Project("Longer"))],
            Estimates(("Known", 30)),
            project => priority.GetValueOrDefault(project.AssemblyName, int.MaxValue)
        );

        await Assert.That(Describe(queue)).IsEqualTo("Long=?, Longer=?, Medium=?, A=?, Known=30");
    }

    [Test]
    public async Task Keeps_each_frameworks_history_and_results_apart()
    {
        var project = Project("Multi", "net10.0", "net11.0");
        var queue = TestQueue.Order([new TestTarget(project, "net11.0"), new(project, "net10.0")], Estimates(("Multi [net11.0]", 90), ("Multi", 200)));

        await Assert.That(Describe(queue)).IsEqualTo("Multi [net10.0]=?, Multi [net11.0]=90");
        await Assert.That(queue[1].ResultsFolder).IsEqualTo(Path.Combine("Multi", "net11.0"));
        await Assert.That(queue[1].EffectiveFramework).IsEqualTo("net11.0");
        await Assert.That(new TestQueueEntry(Project("Single", "net11.0"), "Single", null).EffectiveFramework).IsEqualTo("net11.0");
        await Assert.That(new TestQueueEntry(project, "Multi", null).EffectiveFramework).IsNull();
    }

    [Test]
    public async Task Reads_the_framework_coverage_order_and_shard_settings()
    {
        var configuration = new ConfigurationBuilder()
                           .AddInMemoryCollection(
                                [
                                    new("TestPerFramework", "false"),
                                    new("TestFrameworks:0", "net11.0"),
                                    new("CoverageFrameworks:0", "net11.0"),
                                    new("TestOrder:0", "test/Slow/**"),
                                    new("TestShard", "2/3"),
                                ]
                            )
                           .Build();
        var settings = new TestSettings(null!, null!, configuration);
        var defaults = new TestSettings(null!, null!, new ConfigurationBuilder().Build());

        await Assert.That(settings.PerFramework).IsFalse();
        await Assert.That(settings.Frameworks).IsEquivalentTo(["net11.0"]);
        await Assert.That(settings.CoverageFrameworks).IsEquivalentTo(["net11.0"]);
        await Assert.That(settings.Order).IsEquivalentTo(["test/Slow/**"]);
        await Assert.That(settings.Shard).IsEqualTo(new TestShard(2, 3));
        await Assert.That(defaults.PerFramework).IsTrue();
        await Assert.That(defaults.Frameworks).IsEmpty();
        await Assert.That(defaults.Shard).IsNull();
    }

    [Test]
    public async Task Uses_the_frameworks_history_for_a_project_built_for_that_framework_alone()
    {
        // The default branch ran Multi per framework; a pull request builds it for net11.0 only, so it is one sub module named Multi.
        var queue = TestQueue.Order(
            [new TestTarget(Project("Multi", "net11.0")), new(Project("Other", "net10.0", "net11.0"))],
            Estimates(("Multi [net11.0]", 90), ("Other [net11.0]", 40))
        );

        await Assert.That(Describe(queue)).IsEqualTo("Other=?, Multi=90");
    }

    [Test]
    public async Task Reads_the_build_arguments()
    {
        var configuration = new ConfigurationBuilder()
                           .AddInMemoryCollection([new("BuildArguments:0", "-maxCpuCount:8"), new("BuildArguments:1", "-graphBuild")])
                           .Build();

        await Assert.That(new SolutionSettings(null!, configuration).BuildArguments).IsEquivalentTo(["-maxCpuCount:8", "-graphBuild"]);
        await Assert.That(new SolutionSettings(null!, new ConfigurationBuilder().Build()).BuildArguments).IsEmpty();
    }

    private static string Describe(IEnumerable<TestTarget> targets) =>
        string.Join(", ", targets.Select(target => TestQueue.GetName(target.Project, target.Framework)));

    private static string Describe(IEnumerable<TestQueueEntry> queue) =>
        string.Join(", ", queue.Select(entry => $"{entry.Name}={( entry.Estimate is { } estimate ? estimate.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?" )}"));

    private static SolutionProjectsModule.Project Project(string name, params string[] frameworks) =>
        new(new($"/repo/test/{name}/{name}.csproj"), name, true, [.. frameworks]);

    private static Dictionary<string, TimeSpan> Estimates(params (string Name, int Seconds)[] estimates) =>
        estimates.ToDictionary(z => z.Name, z => TimeSpan.FromSeconds(z.Seconds));
}
