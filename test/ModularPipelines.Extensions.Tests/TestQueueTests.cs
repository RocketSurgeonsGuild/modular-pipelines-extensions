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
                "3 test projects, 2 at a time, longest first by their last runs, one slot taking the shortest first (estimated 1 minute for the 2 with a previous run)",
                "  1. New: no previous run",
                "  2. Slow: 1 minute",
                "  3. Fast: 5 seconds"
            )
        );
    }

    [Test]
    public async Task Describes_path_order_without_history()
    {
        var queue = TestQueue.Order([Project("B"), Project("A")], Estimates());

        await Assert.That(TestQueue.Describe(queue, 4, false)).StartsWith("2 test projects, 4 at a time, in path order: none has a previous run");
    }

    [Test]
    public async Task Runs_tests_per_project_on_half_the_processors_by_default()
    {
        var configuration = new ConfigurationBuilder().Build();
        var settings = new TestSettings(null!, null!, configuration);

        await Assert.That(settings.Mode).IsEqualTo(TestMode.PerProject);
        await Assert.That(settings.Parallelism).IsEqualTo(Math.Max(1, Environment.ProcessorCount / 2));
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

    private static string Describe(IEnumerable<TestQueueEntry> queue) =>
        string.Join(", ", queue.Select(entry => $"{entry.Name}={( entry.Estimate is { } estimate ? estimate.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?" )}"));

    private static SolutionProject Project(string name) =>
        new(new($"/repo/test/{name}/{name}.csproj"), $"test/{name}/{name}.csproj", $"test/{name}/{name}.csproj", name, true);

    private static Dictionary<string, TimeSpan> Estimates(params (string Name, int Seconds)[] estimates) =>
        estimates.ToDictionary(z => z.Name, z => TimeSpan.FromSeconds(z.Seconds));
}
