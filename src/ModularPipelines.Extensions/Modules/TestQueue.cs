using System.Collections.Immutable;
using Humanizer;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>A test project in the order <see cref="TestSolution" /> starts them, with what its history says it takes.</summary>
/// <param name="Project">The test project.</param>
/// <param name="Name">Its sub module name, which its history is kept under.</param>
/// <param name="Estimate">The median of its last runs, or <see langword="null" /> when it has none.</param>
public record TestQueueEntry(SolutionProject Project, string Name, TimeSpan? Estimate);

/// <summary>
///     Orders the test projects so a fixed number of them running at once finishes as early as it can: each free slot takes the next
///     project, and the projects come longest first by their history (longest-processing-time-first scheduling).
/// </summary>
/// <remarks>
///     Projects with no history go first: one of them may be the longest, and starting it last would leave the run waiting on it. Ties
///     go by path, so the same history always gives the same order.
/// </remarks>
internal static class TestQueue
{
    /// <summary>Orders the projects, reading each one's estimate by its sub module name.</summary>
    /// <param name="projects">The test projects to run.</param>
    /// <param name="estimates">The estimates by sub module name.</param>
    public static ImmutableArray<TestQueueEntry> Order(IEnumerable<SolutionProject> projects, IReadOnlyDictionary<string, TimeSpan> estimates) =>
    [
        .. projects
          .Select(project => new TestQueueEntry(project, GetName(project), estimates.TryGetValue(GetName(project), out var estimate) ? estimate : null))
          .OrderBy(entry => entry.Estimate.HasValue)
          .ThenByDescending(entry => entry.Estimate ?? TimeSpan.Zero)
          .ThenBy(entry => entry.Project.RootPath, StringComparer.Ordinal),
    ];

    /// <summary>The sub module name of a project, which is also the key its history is kept under.</summary>
    public static string GetName(SolutionProject project) => project.AssemblyName;

    /// <summary>
    ///     How long the queue should take with <paramref name="parallelism" /> projects at once, as each free slot takes the next
    ///     project: the projects with history only, since the others have no estimate.
    /// </summary>
    public static TimeSpan EstimateDuration(IEnumerable<TestQueueEntry> queue, int parallelism)
    {
        var slots = new PriorityQueue<int, TimeSpan>();
        for (var slot = 0; slot < Math.Max(1, parallelism); slot++) slots.Enqueue(slot, TimeSpan.Zero);

        var end = TimeSpan.Zero;
        foreach (var estimate in queue.Select(entry => entry.Estimate).OfType<TimeSpan>())
        {
            slots.TryDequeue(out var slot, out var free);
            var finished = free + estimate;
            end = finished > end ? finished : end;
            slots.Enqueue(slot, finished);
        }

        return end;
    }

    /// <summary>Describes the order for the log.</summary>
    public static string Describe(IReadOnlyCollection<TestQueueEntry> queue, int parallelism)
    {
        var known = queue.Count(entry => entry.Estimate.HasValue);
        var summary = known == 0
            ? $"{queue.Count} test projects, {parallelism} at a time, in path order: none has a previous run"
            : $"{queue.Count} test projects, {parallelism} at a time, longest first by their last runs"
            + $" (estimated {EstimateDuration(queue, parallelism).Humanize(2)}{( known < queue.Count ? $" for the {known} with a previous run" : "" )})";

        return summary
             + Environment.NewLine
             + string.Join(
                   Environment.NewLine,
                   queue.Select((entry, index) => $"  {index + 1}. {entry.Name}: {( entry.Estimate is { } estimate ? estimate.Humanize(2) : "no previous run" )}")
               );
    }
}
