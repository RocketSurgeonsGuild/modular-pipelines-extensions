using System.Collections.Immutable;
using Humanizer;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     A test project, or one target framework of it, in the order <see cref="TestSolutionModule" /> queues them, with what its history
///     says it takes.
/// </summary>
/// <param name="Project">The test project.</param>
/// <param name="Name">Its sub module name, which its history is kept under.</param>
/// <param name="Estimate">The median of its last runs, or <see langword="null" /> when it has none.</param>
/// <param name="Framework">The target framework it tests, or <see langword="null" /> for every framework of the project at once.</param>
public record TestQueueEntry(SolutionProjectsModule.Project Project, string Name, TimeSpan? Estimate, string? Framework = null)
{
    /// <summary>The folder, under the test artifacts, its TRX and coverage files go to.</summary>
    public string ResultsFolder => Framework is null ? Project.AssemblyName : Path.Combine(Project.AssemblyName, Framework);

    /// <summary>The one framework the run tests: <see cref="Framework" />, or the project's only one; <see langword="null" /> for several.</summary>
    public string? EffectiveFramework => Framework ?? ( Project.Frameworks is [var only] ? only : null );
}

/// <summary>A test project, and the target framework to test when only one of several is.</summary>
/// <param name="Project">The test project.</param>
/// <param name="Framework">The framework, or <see langword="null" /> for all of them at once.</param>
public record TestTarget(SolutionProjectsModule.Project Project, string? Framework = null);

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
        Order(projects.Select(project => new TestTarget(project)), estimates);

    /// <summary>Orders the test sub modules, reading each one's estimate by its sub module name.</summary>
    /// <param name="targets">The projects, or projects and frameworks, to run.</param>
    /// <param name="estimates">The estimates by sub module name.</param>
    /// <param name="priority">
    ///     Where a sub module with no history goes among the others with none, lower first (see <see cref="TestSettings.Order" />); path
    ///     order when <see langword="null" />.
    /// </param>
    public static ImmutableArray<TestQueueEntry> Order(
        IEnumerable<TestTarget> targets,
        IReadOnlyDictionary<string, TimeSpan> estimates,
        Func<SolutionProjectsModule.Project, int>? priority = null
    ) =>
    [
        .. targets
          .Select(target =>
               {
                   var name = GetName(target.Project, target.Framework);
                   return new TestQueueEntry(target.Project, name, estimates.TryGetValue(name, out var estimate) ? estimate : null, target.Framework);
               }
           )
          .OrderBy(entry => entry.Estimate.HasValue)
          .ThenBy(entry => entry.Estimate.HasValue || priority is null ? 0 : priority(entry.Project))
          .ThenByDescending(entry => entry.Estimate ?? TimeSpan.Zero)
          .ThenBy(entry => entry.Project.ProjectPath.Path, StringComparer.Ordinal)
          .ThenBy(entry => entry.Framework, StringComparer.Ordinal),
    ];

    /// <summary>The sub module name of a project, which is also the key its history is kept under.</summary>
    public static string GetName(SolutionProjectsModule.Project project) => project.AssemblyName;

    /// <summary>The sub module name of a project, or of one of its frameworks, which is also the key its history is kept under.</summary>
    public static string GetName(SolutionProjectsModule.Project project, string? framework) =>
        framework is null ? project.AssemblyName : $"{project.AssemblyName} [{framework}]";

    /// <summary>
    ///     What to run of each test project: one target per framework when <paramref name="perFramework" /> is set and the project has
    ///     several, only the <paramref name="frameworks" /> asked for (a project with none of them is left out), and the project as a whole
    ///     otherwise.
    /// </summary>
    public static IEnumerable<TestTarget> Expand(IEnumerable<SolutionProjectsModule.Project> projects, bool perFramework, IReadOnlyCollection<string> frameworks)
    {
        foreach (var project in projects)
        {
            var all = project.Frameworks;
            var selected = frameworks.Count == 0 ? all : [.. all.Where(framework => frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))];
            if (all.Length > 0 && selected.Length == 0) continue;

            if (( perFramework && selected.Length > 1 ) || selected.Length < all.Length)
            {
                foreach (var framework in selected) yield return new(project, framework);
            }
            else
            {
                yield return new(project);
            }
        }
    }

    /// <summary>The targets of one shard: every <see cref="TestShard.Count" />th in path and framework order, so the split needs no history.</summary>
    public static IEnumerable<TestTarget> InShard(IEnumerable<TestTarget> targets, TestShard? shard) =>
        shard is null
            ? targets
            : targets
             .OrderBy(target => target.Project.ProjectPath.Path, StringComparer.Ordinal)
             .ThenBy(target => target.Framework, StringComparer.Ordinal)
             .Where((_, index) => index % shard.Count == shard.Index - 1);

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
    /// <param name="queue">The queue, in <see cref="Order(IEnumerable{TestTarget}, IReadOnlyDictionary{string, TimeSpan}, Func{SolutionProjectsModule.Project, int}?)" />'s order.</param>
    /// <param name="parallelism">How many run at once.</param>
    /// <param name="fastLane">Whether the first slot is a fast lane.</param>
    /// <param name="hasOrder">Whether <see cref="TestSettings.Order" /> names projects, which go first among those with no previous run.</param>
    public static string Describe(IReadOnlyCollection<TestQueueEntry> queue, int parallelism, bool fastLane, bool hasOrder = false)
    {
        var known = queue.Count(entry => entry.Estimate.HasValue);
        var lanes = fastLane ? ", one slot taking the shortest first" : "";
        var unknownOrder = hasOrder ? "TestOrder first, then path order" : "path order";
        var summary = known == 0
            ? $"{queue.Count} test projects, {parallelism} at a time, in {unknownOrder}: none has a previous run"
            : $"{queue.Count} test projects, {parallelism} at a time, longest first by their last runs{lanes}"
            + $" (estimated {EstimateDuration(queue, parallelism, fastLane).Humanize(2)}{( known < queue.Count ? $" for the {known} with a previous run; the others start first, in {unknownOrder}" : "" )})";

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
