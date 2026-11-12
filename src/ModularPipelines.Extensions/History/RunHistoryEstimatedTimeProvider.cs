using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ModularPipelines.Reporting;

namespace Rocket.Surgery.ModularPipelines.Extensions.History;

/// <summary>
///     Estimates a module's duration from run history (the median of its last successful runs), and a sub module's from the last
///     duration kept in <see cref="RocketHistoryOptions.EstimatesDirectory" />.
/// </summary>
/// <remarks>
///     In a distributed run each module's duration is saved by the machine that ran it, but the master plans the critical path from
///     its own estimates. Run history is written by the master and holds every module, wherever it ran, so the master estimates from
///     it. Run history has no sub modules, so those, and modules with no history yet, fall back to the last duration saved here, in a
///     folder under the repository so the same CI cache restores it.
/// </remarks>
public sealed class RunHistoryEstimatedTimeProvider(
    IRunHistoryReader history,
    RocketHistoryOptions options,
    ILogger<RunHistoryEstimatedTimeProvider> logger
) : IModuleEstimatedTimeProvider
{
    // ModularPipelines' own default.
    private static readonly TimeSpan DefaultEstimate = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory = options.Resolve(options.EstimatesDirectory);

    /// <inheritdoc />
    public async Task<TimeSpan> GetModuleEstimatedTimeAsync(ModuleId moduleId, CancellationToken cancellationToken = default)
    {
        if (await FromHistoryAsync(moduleId, cancellationToken).ConfigureAwait(false) is { } fromHistory) return fromHistory;

        var entry = await ReadAsync(moduleId, cancellationToken).ConfigureAwait(false);
        return entry?.Duration ?? DefaultEstimate;
    }

    /// <inheritdoc />
    public Task SaveModuleTimeAsync(ModuleId moduleId, TimeSpan duration, CancellationToken cancellationToken = default) =>
        UpdateAsync(moduleId, entry => entry with { Duration = duration }, cancellationToken);

    /// <inheritdoc />
    public async Task<IEnumerable<SubModuleEstimation>> GetSubModuleEstimatedTimesAsync(ModuleId moduleId, CancellationToken cancellationToken = default)
    {
        var entry = await ReadAsync(moduleId, cancellationToken).ConfigureAwait(false);
        return entry?.SubModules.Select(pair => new SubModuleEstimation(pair.Key, pair.Value)) ?? [];
    }

    /// <inheritdoc />
    public Task SaveSubModuleTimeAsync(ModuleId moduleId, SubModuleEstimation subModuleEstimation, CancellationToken cancellationToken = default) =>
        UpdateAsync(
            moduleId,
            entry => entry with
            {
                SubModules = new(entry.SubModules, StringComparer.Ordinal)
                {
                    [subModuleEstimation.SubModuleName] = subModuleEstimation.EstimatedDuration,
                },
            },
            cancellationToken
        );

    private async Task<TimeSpan?> FromHistoryAsync(ModuleId moduleId, CancellationToken cancellationToken)
    {
        if (!options.Enabled || options.EstimateSamples <= 0) return null;

        try
        {
            var samples = await history.GetModuleDurationTrendAsync(moduleId, options.EstimateSamples, cancellationToken).ConfigureAwait(false);
            var durations = samples.Where(s => s.Status == ModuleStatus.Succeeded).Select(s => s.Duration).Order().ToArray();
            return durations.Length == 0 ? null : durations[durations.Length / 2];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug(ex, "Could not read the run history of {ModuleId}", moduleId.Value);
            return null;
        }
    }

    private string GetPath(ModuleId moduleId) =>
        Path.Combine(_directory, $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(moduleId.Value)))[..32]}.json");

    private async Task<EstimateEntry?> ReadAsync(ModuleId moduleId, CancellationToken cancellationToken)
    {
        var path = GetPath(moduleId);
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, EstimateEntryContext.Default.EstimateEntry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug(ex, "Could not read the estimates of {ModuleId}", moduleId.Value);
            return null;
        }
    }

    private async Task UpdateAsync(ModuleId moduleId, Func<EstimateEntry, EstimateEntry> update, CancellationToken cancellationToken)
    {
        // Sub modules of one module finish side by side and share its file.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = update(await ReadAsync(moduleId, cancellationToken).ConfigureAwait(false) ?? new EstimateEntry(moduleId.Value, null, []));
            Directory.CreateDirectory(_directory);
            var path = GetPath(moduleId);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, entry, EstimateEntryContext.Default.EstimateEntry, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug(ex, "Could not save the estimates of {ModuleId}", moduleId.Value);
        }
        finally
        {
            _gate.Release();
        }
    }
}

internal sealed record EstimateEntry(string ModuleId, TimeSpan? Duration, Dictionary<string, TimeSpan> SubModules);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EstimateEntry))]
internal sealed partial class EstimateEntryContext : JsonSerializerContext;
