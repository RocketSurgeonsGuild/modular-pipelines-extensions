using Microsoft.Extensions.Logging.Abstractions;
using Rocket.Surgery.ModularPipelines.Extensions.History;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

namespace Rocket.Surgery.ModularPipelines.Extensions.Tests;

public class TestShardPartitionerTests
{
    private static readonly TestShardItem[] WithHistory =
    [
        new("test/A.Tests/A.Tests.csproj", TimeSpan.FromMinutes(10)),
        new("test/B.Tests/B.Tests.csproj", TimeSpan.FromMinutes(7)),
        new("test/C.Tests/C.Tests.csproj", TimeSpan.FromMinutes(6)),
        new("test/D.Tests/D.Tests.csproj", TimeSpan.FromMinutes(5)),
        new("test/E.Tests/E.Tests.csproj", TimeSpan.FromMinutes(2)),
        new("test/F.Tests/F.Tests.csproj", TimeSpan.FromMinutes(1)),
    ];

    [Test]
    public async Task Places_every_project_in_exactly_one_shard()
    {
        var shards = TestShardPartitioner.Partition(WithHistory, 4);

        await Assert.That(shards.Select(shard => shard.Number)).IsEquivalentTo([1, 2, 3, 4]);
        await Assert.That(shards.SelectMany(shard => shard.Projects).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(WithHistory.Select(item => item.Key).Order(StringComparer.Ordinal));
    }

    [Test]
    public async Task Gives_the_same_split_whatever_the_input_order()
    {
        var forward = TestShardPartitioner.Partition(WithHistory, 3);
        var backward = TestShardPartitioner.Partition(WithHistory.Reverse(), 3);

        await Assert.That(Describe(backward)).IsEqualTo(Describe(forward));
    }

    [Test]
    public async Task Balances_by_duration_longest_first()
    {
        // 10 | 7+2+1 | 6+5: no shard gets more than the longest project needs.
        var shards = TestShardPartitioner.Partition(WithHistory, 3);

        await Assert.That(Describe(shards)).IsEqualTo(
            "1: test/A.Tests/A.Tests.csproj = 00:10:00; "
          + "2: test/B.Tests/B.Tests.csproj, test/E.Tests/E.Tests.csproj, test/F.Tests/F.Tests.csproj = 00:10:00; "
          + "3: test/C.Tests/C.Tests.csproj, test/D.Tests/D.Tests.csproj = 00:11:00"
        );
    }

    [Test]
    public async Task Deals_round_robin_by_path_without_history()
    {
        var shards = TestShardPartitioner.Partition(WithHistory.Reverse().Select(item => item with { Duration = null }), 4);

        await Assert.That(Describe(shards)).IsEqualTo(
            "1: test/A.Tests/A.Tests.csproj, test/E.Tests/E.Tests.csproj = 00:00:00; "
          + "2: test/B.Tests/B.Tests.csproj, test/F.Tests/F.Tests.csproj = 00:00:00; "
          + "3: test/C.Tests/C.Tests.csproj = 00:00:00; "
          + "4: test/D.Tests/D.Tests.csproj = 00:00:00"
        );
    }

    [Test]
    public async Task Counts_a_project_without_history_as_the_median()
    {
        TestShardItem[] items =
        [
            new("a", TimeSpan.FromMinutes(1)),
            new("b", TimeSpan.FromMinutes(3)),
            new("c", TimeSpan.FromMinutes(5)),
            new("new", null),
        ];

        var shards = TestShardPartitioner.Partition(items, 2);

        // new counts as 3 minutes: c (5) | b (3), new (3), a (1) goes to the lighter first shard.
        await Assert.That(Describe(shards)).IsEqualTo("1: a, c = 00:06:00; 2: b, new = 00:06:00");
    }

    [Test]
    public async Task Leaves_extra_shards_empty_and_ignores_duplicates()
    {
        var shards = TestShardPartitioner.Partition([new("a", null), new("a", null)], 3);

        await Assert.That(Describe(shards)).IsEqualTo("1: a = 00:00:00; 2:  = 00:00:00; 3:  = 00:00:00");
    }

    [Test]
    public async Task Splits_by_the_latest_run_rather_than_the_median()
    {
        using var directory = new TemporaryDirectory();
        var history = CreateHistory(directory);
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        // A took 9 minutes twice and then 1 minute: its median is 9 minutes, its last run 1 minute.
        await history.RecordAsync(new("A", TimeSpan.FromMinutes(9), true, start));
        await history.RecordAsync(new("A", TimeSpan.FromMinutes(9), true, start.AddDays(1)));
        await history.RecordAsync(new("B", TimeSpan.FromMinutes(5), true, start.AddDays(1)));
        await history.RecordAsync(new("C", TimeSpan.FromMinutes(4), true, start.AddDays(1)));
        await history.RecordAsync(new("A", TimeSpan.FromMinutes(1), true, start.AddDays(2)));

        var shards = TestShardPartitioner.Partition(await GetItemsAsync(history, "A", "B", "C"), 2);

        // By the median it would be A (9) | B, C (9).
        await Assert.That(Describe(shards)).IsEqualTo("1: b = 00:05:00; 2: a, c = 00:05:00");
    }

    [Test]
    public async Task Uses_the_last_run_of_a_project_that_failed()
    {
        using var directory = new TemporaryDirectory();
        var history = CreateHistory(directory);
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        await history.RecordAsync(new("A", TimeSpan.FromMinutes(9), true, start));
        await history.RecordAsync(new("A", TimeSpan.FromMinutes(2), false, start.AddDays(1)));
        // Recorded late, but finished earlier, so it does not replace the failed run.
        await history.RecordAsync(new("A", TimeSpan.FromMinutes(7), true, start.AddHours(12)));

        // Read back by a new instance, as the next run does.
        var lastRuns = await CreateHistory(directory).GetLastRunsAsync();

        await Assert.That(lastRuns["A"]).IsEqualTo(new TestProjectRun("A", TimeSpan.FromMinutes(2), false, start.AddDays(1)));
    }

    [Test]
    public async Task Counts_a_project_without_a_last_run_as_the_median_of_the_last_runs()
    {
        using var directory = new TemporaryDirectory();
        var history = CreateHistory(directory);
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        await history.RecordAsync(new("A", TimeSpan.FromMinutes(1), true, start));
        await history.RecordAsync(new("B", TimeSpan.FromMinutes(3), false, start));
        await history.RecordAsync(new("C", TimeSpan.FromMinutes(5), true, start));

        var items = await GetItemsAsync(history, "A", "B", "C", "New");
        var shards = TestShardPartitioner.Partition(items, 2);

        // New counts as 3 minutes: c (5) | b (3), new (3), a (1) goes to the lighter first shard.
        await Assert.That(TestShardPartitioner.GetFallback(items)).IsEqualTo(TimeSpan.FromMinutes(3));
        await Assert.That(Describe(shards)).IsEqualTo("1: a, c = 00:06:00; 2: b, new = 00:06:00");
    }

    [Test]
    public async Task Deals_round_robin_without_a_previous_run()
    {
        using var directory = new TemporaryDirectory();
        var items = await GetItemsAsync(CreateHistory(directory), "D", "C", "B", "A");

        var shards = TestShardPartitioner.Partition(items, 2);

        await Assert.That(Describe(shards)).IsEqualTo("1: a, c = 00:00:00; 2: b, d = 00:00:00");
        await Assert.That(TestShardPlan.Describe(shards, items, await CreateHistory(directory).GetLastRunsAsync()))
                    .StartsWith("Test projects split between 2 shards round-robin by path: no test project has a previous run");
    }

    [Test]
    public async Task Logs_the_last_run_of_each_project()
    {
        using var directory = new TemporaryDirectory();
        var history = CreateHistory(directory);
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        await history.RecordAsync(new("A", TimeSpan.FromSeconds(30), true, start));
        await history.RecordAsync(new("B", TimeSpan.FromSeconds(10), false, start));

        var items = await GetItemsAsync(history, "A", "B", "New");
        var shards = TestShardPartitioner.Partition(items, 2);

        // The median of 10 and 30 seconds is the upper one; b goes to the first of two equal shards.
        await Assert.That(TestShardPlan.Describe(shards, items, await history.GetLastRunsAsync())).IsEqualTo(
            "Test projects split between 2 shards by the duration of each project's last run:"
          + $"{Environment.NewLine}  shard 1 (estimated 40.0s): a (last run 30.0s), b (last run 10.0s, failed)"
          + $"{Environment.NewLine}  shard 2 (estimated 30.0s): new (no previous run, counted as the median 30.0s)"
        );
    }

    [Test]
    public async Task Gives_the_same_split_from_the_same_last_runs()
    {
        using var directory = new TemporaryDirectory();
        var history = CreateHistory(directory);
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var (name, minutes) in new[] { ("A", 4), ("B", 4), ("C", 4), ("D", 2) })
        {
            await history.RecordAsync(new(name, TimeSpan.FromMinutes(minutes), true, start));
        }

        var first = TestShardPartitioner.Partition(await GetItemsAsync(history, "A", "B", "C", "D"), 3);
        var second = TestShardPartitioner.Partition(await GetItemsAsync(CreateHistory(directory), "D", "C", "B", "A"), 3);

        await Assert.That(Describe(second)).IsEqualTo(Describe(first));
        await Assert.That(Describe(first)).IsEqualTo("1: a, d = 00:06:00; 2: b = 00:04:00; 3: c = 00:04:00");
    }

    private static TestProjectHistory CreateHistory(TemporaryDirectory directory) =>
        new(new() { RootDirectory = directory.Path }, NullLogger<TestProjectHistory>.Instance);

    // Projects keyed by a lower case path, as the plan keys them by path and looks their runs up by sub module name.
    private static async Task<IReadOnlyList<TestShardItem>> GetItemsAsync(TestProjectHistory history, params string[] names) =>
        TestShardPlan.GetItems(names.Select(name => (name.ToLowerInvariant(), name)), await history.GetLastRunsAsync());

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("test-shards-").FullName;

        public void Dispose() => Directory.Delete(Path, true);
    }

    private static string Describe(IEnumerable<TestShardAssignment> shards) =>
        string.Join("; ", shards.Select(shard => $"{shard.Number}: {string.Join(", ", shard.Projects)} = {shard.Estimate}"));
}
