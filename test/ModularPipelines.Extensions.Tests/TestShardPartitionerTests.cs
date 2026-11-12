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

    private static string Describe(IEnumerable<TestShardAssignment> shards) =>
        string.Join("; ", shards.Select(shard => $"{shard.Number}: {string.Join(", ", shard.Projects)} = {shard.Estimate}"));
}
