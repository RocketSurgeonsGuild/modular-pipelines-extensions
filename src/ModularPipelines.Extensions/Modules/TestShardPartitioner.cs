using System.Collections.Immutable;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>A test project to place in a shard: its stable key (the path relative to the root) and, when known, how long it took.</summary>
/// <param name="Key">The project's path relative to the root directory.</param>
/// <param name="Duration">Its last known duration, or <see langword="null" /> when it has no history.</param>
internal sealed record TestShardItem(string Key, TimeSpan? Duration);

/// <summary>Splits test projects between a fixed number of shards, the same way every time for the same input.</summary>
internal static class TestShardPartitioner
{
    /// <summary>
    ///     Places every item in exactly one of <paramref name="shardCount" /> shards. With no durations at all the items are dealt
    ///     round-robin in ordinal order of their keys. Otherwise each goes, longest first, to the shard with the least work so far
    ///     (longest processing time first); an item without a duration counts as the median of the known ones.
    /// </summary>
    /// <returns>The keys of each shard, in ordinal order, and the estimated work of each shard.</returns>
    public static ImmutableArray<TestShardAssignment> Partition(IEnumerable<TestShardItem> items, int shardCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(shardCount, 1);

        // Ordinal order first, so neither the input order nor duplicate keys can change the outcome.
        var sorted = items.DistinctBy(item => item.Key, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var keys = Enumerable.Range(0, shardCount).Select(_ => new List<string>()).ToArray();
        var loads = new TimeSpan[shardCount];

        var known = sorted.Where(item => item.Duration is not null).Select(item => item.Duration!.Value).Order().ToArray();
        if (known.Length == 0)
        {
            for (var i = 0; i < sorted.Length; i++)
            {
                keys[i % shardCount].Add(sorted[i].Key);
            }
        }
        else
        {
            var fallback = known[known.Length / 2];
            foreach (var (key, duration) in sorted
                                .Select(item => (item.Key, Duration: item.Duration ?? fallback))
                                .OrderByDescending(item => item.Duration)
                                .ThenBy(item => item.Key, StringComparer.Ordinal))
            {
                var shard = 0;
                for (var i = 1; i < shardCount; i++)
                {
                    if (loads[i] < loads[shard]) shard = i;
                }

                keys[shard].Add(key);
                loads[shard] += duration;
            }
        }

        return
        [
            .. keys.Select((shard, index) => new TestShardAssignment(
                    index + 1,
                    [.. shard.Order(StringComparer.Ordinal)],
                    loads[index]
                )
            ),
        ];
    }
}

/// <summary>The test projects one shard runs.</summary>
/// <param name="Number">The shard's number, from one.</param>
/// <param name="Projects">The projects, as paths relative to the root directory.</param>
/// <param name="Estimate">The sum of their estimated durations; zero without history.</param>
public sealed record TestShardAssignment(int Number, ImmutableList<string> Projects, TimeSpan Estimate);
