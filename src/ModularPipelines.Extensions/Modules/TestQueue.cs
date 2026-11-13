using System.Collections.Immutable;
using Humanizer;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>A test project in the order <see cref="TestSolutionModule" /> queues them, with what its history says it takes.</summary>
/// <param name="Project">The test project.</param>
/// <param name="Name">Its sub module name, which its history is kept under.</param>
/// <param name="Estimate">The median of its last runs, or <see langword="null" /> when it has none.</param>
public record TestQueueEntry(SolutionProjectsModule.Project Project, string Name, TimeSpan? Estimate);

/// <summary>
///     Orders the test projects so a fixed number of them running at once finishes as early as it can, and gives the fast ones a slot of
///     their own.
/// </summary>
/// <remarks>
///     <para>
///         The queue runs longest first by the projects' history, and each slot takes its next project when it is free, so a wrong
///         estimate only shifts the work between the slots. With more than one slot, the first can be a fast lane: it takes the
///         shortest project from the other end, so the quick projects report early and fill the time the long ones take, while the
///         other slots take the longest. The fast lane is used when the estimates say the run finishes no later with it than without
///         (<see cref="UsesFastLane" />): it can leave a medium project to start late in it.
///     </para>
///     <para>
///         Projects with no history go first: one of them may be the longest, and starting it last would leave the run waiting on it. Ties
///         go by path, so the same history always gives the same order.
///     </para>
/// </remarks>
internal static class TestQueue
{
    /// <summary>Orders the projects, reading each one's estimate by its sub module name.</summary>
    /// <param name="projects">The test projects to run.</param>
    /// <param name="estimates">The estimates by sub module name.</param>
    public static ImmutableArray<TestQueueEntry> Order(IEnumerable<SolutionProjectsModule.Project> projects, IReadOnlyDictionary<string, TimeSpan> estimates) =>
    [
        .. projects
          .Select(project => new TestQueueEntry(project, GetName(project), estimates.TryGetValue(GetName(project), out var estimate) ? estimate : null))
          .OrderBy(entry => entry.Estimate.HasValue)
          .ThenByDescending(entry => entry.Estimate ?? TimeSpan.Zero)
          .ThenBy(entry => entry.Project.ProjectPath.Path, StringComparer.Ordinal),
    ];

    /// <summary>The sub module name of a project, which is also the key its history is kept under.</summary>
    public static string GetName(SolutionProjectsModule.Project project) => project.AssemblyName;

    /// <summary>
    ///     Whether the first slot is a fast lane: there is more than one slot, more projects with a previous run than slots (otherwise
    ///     every project starts at once, or the estimates have too little to compare), and the estimates say the run finishes no later with
    ///     it than with every slot taking the longest.
    /// </summary>
    /// <remarks>
    ///     Without enough history both estimates are zero and would always pick the fast lane, which can start a long project with no
    ///     history late.
    /// </remarks>
    public static bool UsesFastLane(IReadOnlyCollection<TestQueueEntry> queue, int parallelism) =>
        parallelism > 1
     && queue.Count > parallelism
     && queue.Count(entry => entry.Estimate.HasValue) > parallelism
     && EstimateDuration(queue, parallelism, true) <= EstimateDuration(queue, parallelism, false);

    /// <summary>
    ///     How long the queue should take with <paramref name="parallelism" /> slots, as each free slot takes its next project, the first
    ///     taking the shortest when <paramref name="fastLane" /> is set: the projects with history only, since the others have no
    ///     estimate.
    /// </summary>
    public static TimeSpan EstimateDuration(IEnumerable<TestQueueEntry> queue, int parallelism, bool fastLane)
    {
        parallelism = Math.Max(1, parallelism);
        var lanes = new TestLanes([.. queue.Where(entry => entry.Estimate.HasValue)]);
        // By when each slot is free, then by slot, so the simulation is the same every time.
        var slots = new PriorityQueue<int, (TimeSpan Free, int Slot)>();
        for (var slot = 0; slot < parallelism; slot++) slots.Enqueue(slot, (TimeSpan.Zero, slot));

        var end = TimeSpan.Zero;
        while (slots.TryDequeue(out var slot, out var priority))
        {
            if (!lanes.TryTake(fastLane && slot == 0, out var entry)) continue;

            var finished = priority.Free + entry.Estimate.GetValueOrDefault();
            end = finished > end ? finished : end;
            slots.Enqueue(slot, (finished, slot));
        }

        return end;
    }

    /// <summary>Describes the order for the log.</summary>
    public static string Describe(IReadOnlyCollection<TestQueueEntry> queue, int parallelism, bool fastLane)
    {
        var known = queue.Count(entry => entry.Estimate.HasValue);
        var lanes = fastLane ? ", one slot taking the shortest first" : "";
        var summary = known == 0
            ? $"{queue.Count} test projects, {parallelism} at a time, in path order: none has a previous run"
            : $"{queue.Count} test projects, {parallelism} at a time, longest first by their last runs{lanes}"
            + $" (estimated {EstimateDuration(queue, parallelism, fastLane).Humanize(2)}{( known < queue.Count ? $" for the {known} with a previous run" : "" )})";

        return summary
             + Environment.NewLine
             + string.Join(
                   Environment.NewLine,
                   queue.Select((entry, index) => $"  {index + 1}. {entry.Name}: {( entry.Estimate is { } estimate ? estimate.Humanize(2) : "no previous run" )}")
               );
    }
}

/// <summary>The queue the slots take their projects from: the longest from the front, the fast lane the shortest from the back.</summary>
/// <param name="queue">The projects in <see cref="TestQueue.Order" />'s order.</param>
internal sealed class TestLanes(IReadOnlyList<TestQueueEntry> queue)
{
    private readonly Lock _lock = new();
    private int _front;
    private int _back = queue.Count - 1;

    /// <summary>Takes the next project, or returns <see langword="false" /> once every project was taken.</summary>
    /// <param name="shortest">Whether to take the shortest project left instead of the longest.</param>
    /// <param name="entry">The project.</param>
    public bool TryTake(bool shortest, [MaybeNullWhen(false)] out TestQueueEntry entry)
    {
        lock (_lock)
        {
            if (_front > _back)
            {
                entry = null;
                return false;
            }

            entry = shortest ? queue[_back--] : queue[_front++];
            return true;
        }
    }
}
