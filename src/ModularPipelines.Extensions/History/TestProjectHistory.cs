using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Rocket.Surgery.ModularPipelines.Extensions.History;

/// <summary>The last run of one test project.</summary>
/// <param name="Name">The project's sub module name: its assembly name, with the target framework when <c>TestFramework</c> is set.</param>
/// <param name="Duration">How long its <c>dotnet test</c> took.</param>
/// <param name="Succeeded">Whether its tests passed.</param>
/// <param name="FinishedAt">When it finished.</param>
[PublicAPI]
public sealed record TestProjectRun(string Name, TimeSpan Duration, bool Succeeded, DateTimeOffset FinishedAt);

/// <summary>
///     The last run of each test project, kept in <c>test-projects.json</c> under <see cref="RocketHistoryOptions.EstimatesDirectory" />.
///     <see cref="Modules.TestSolution" /> and the test shards record every project they run, and <see cref="Modules.TestShardPlan" />
///     splits the projects between the shards by it.
/// </summary>
/// <remarks>
///     A project is recorded whether it passed or failed, but not when the run was cancelled before it finished, so its entry is always
///     the most recent run that finished. Run history cannot serve here: it only records whole modules, and a project's sub module
///     moves between <see cref="Modules.TestSolution" /> and the shards from one run to the next.
/// </remarks>
[PublicAPI]
public sealed class TestProjectHistory(RocketHistoryOptions options, ILogger<TestProjectHistory> logger)
{
    /// <summary>The file's name in <see cref="RocketHistoryOptions.EstimatesDirectory" />.</summary>
    public const string FileName = "test-projects.json";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(options.Resolve(options.EstimatesDirectory), FileName);

    /// <summary>The last run of each project that has one, by <see cref="TestProjectRun.Name" />.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<ImmutableDictionary<string, TestProjectRun>> GetLastRunsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Records a project's run, replacing its previous one unless that finished later.</summary>
    /// <param name="run">The run.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task RecordAsync(TestProjectRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        // Projects of one module, and the shards, finish side by side and share the file.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runs = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (runs.TryGetValue(run.Name, out var previous) && previous.FinishedAt > run.FinishedAt) return;

            runs = runs.SetItem(run.Name, run);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporary))
            {
                // Sorted, so the file only changes where a project's run did.
                TestProjectRun[] sorted = [.. runs.Values.OrderBy(value => value.Name, StringComparer.Ordinal)];
                await JsonSerializer.SerializeAsync(stream, sorted, TestProjectHistoryContext.Default.TestProjectRunArray, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug(ex, "Could not record the run of {Project}", run.Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ImmutableDictionary<string, TestProjectRun>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return ImmutableDictionary<string, TestProjectRun>.Empty.WithComparers(StringComparer.Ordinal);

        try
        {
            await using var stream = File.OpenRead(_path);
            var runs = await JsonSerializer.DeserializeAsync(stream, TestProjectHistoryContext.Default.TestProjectRunArray, cancellationToken).ConfigureAwait(false) ?? [];
            return runs
                  .GroupBy(run => run.Name, StringComparer.Ordinal)
                  .ToImmutableDictionary(group => group.Key, group => group.MaxBy(run => run.FinishedAt)!, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug(ex, "Could not read the last test project runs from {Path}", _path);
            return ImmutableDictionary<string, TestProjectRun>.Empty.WithComparers(StringComparer.Ordinal);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TestProjectRun[]))]
internal sealed partial class TestProjectHistoryContext : JsonSerializerContext;
