using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModularPipelines.Caching;
using ModularPipelines.Extensions;

namespace Rocket.Surgery.ModularPipelines.Extensions.History;

/// <summary>Turns on run history and history-based duration estimates.</summary>
[PublicAPI]
public static class RocketHistoryExtensions
{
    /// <summary>
    ///     Keeps run history under the repository (<c>.modularpipelines/run-history</c> by default) with a stable pipeline identity, and
    ///     estimates module and sub module durations from it, which orders the critical path and the progress display. The
    ///     <c>History</c> configuration section supplies the defaults; <c>History:Enabled=false</c> turns run history off.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="configure">Changes the options read from configuration.</param>
    /// <returns>The same builder.</returns>
    public static PipelineBuilder AddRocketHistory(this PipelineBuilder builder, Func<RocketHistoryOptions, RocketHistoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = RocketHistoryOptions.FromConfiguration(builder.Configuration, builder.WorkingDirectory);
        if (configure is not null) options = configure(options);

        if (!options.Enabled)
        {
            builder.ConfigureOptions(o => o with { RunReport = o.RunReport with { HistoryRetention = 0 } });
            return builder;
        }

        builder.ConfigureOptions(o => o with
            {
                RunReport = o.RunReport with
                {
                    HistoryDirectory = options.Resolve(options.HistoryDirectory),
                    HistoryRetention = options.Retention,
                    GlobalHistoryRetention = o.RunReport.GlobalHistoryRetention > 0
                        ? Math.Max(o.RunReport.GlobalHistoryRetention, options.Retention)
                        : 0,
                    PipelineIdentity = options.PipelineIdentity ?? o.RunReport.PipelineIdentity,
                },
            }
        );

        builder.Services.RemoveAll<RocketHistoryOptions>();
        builder.Services.AddSingleton(options);
        builder.AddModuleEstimatedTimeProvider<RunHistoryEstimatedTimeProvider>();

        if (options.ModuleCache)
        {
            builder.AddModuleCache<FileSystemModuleCache>(o => o with { CacheDirectory = options.Resolve(options.ModuleCacheDirectory) });
        }

        return builder;
    }
}
