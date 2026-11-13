using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModularPipelines.Caching;

namespace Rocket.Surgery.ModularPipelines.Extensions.History;

/// <summary>Keeps ModularPipelines' run history, duration estimates and module cache under the repository.</summary>
[PublicAPI]
public static class RocketHistoryExtensions
{
    /// <summary>
    ///     Keeps everything ModularPipelines remembers between runs under <c>.modularpipelines/</c> in the repository, where a CI cache can
    ///     restore it, instead of the user's application-data folders:
    ///     <list type="bullet">
    ///         <item>run history (<c>run-history</c>), grouped under a stable pipeline identity;</item>
    ///         <item>
    ///             duration estimates (<c>estimates</c>): the median of a module's last successful runs, or else its last duration, which
    ///             order the critical path and the progress display;
    ///         </item>
    ///         <item>the module cache (<c>module-cache</c>), which restores the modules that declare their cache inputs.</item>
    ///     </list>
    ///     The <c>History</c> configuration section supplies the defaults. <c>History:Enabled=false</c> turns run history off and
    ///     <c>History:ModuleCache:Enabled=false</c> the module cache; the estimates are always kept.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <param name="configure">Changes the options read from configuration.</param>
    /// <returns>The same builder.</returns>
    public static PipelineBuilder AddRocketHistory(this PipelineBuilder builder, Func<RocketHistoryOptions, RocketHistoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = RocketHistoryOptions.FromConfiguration(builder.Configuration, builder.WorkingDirectory);
        if (configure is not null) options = configure(options);

        builder.ConfigureOptions(o => o with
        {
            RunReport = options.Enabled
                ? o.RunReport with
                {
                    HistoryDirectory = options.Resolve(options.HistoryDirectory),
                    HistoryRetention = options.Retention,
                    GlobalHistoryRetention = o.RunReport.GlobalHistoryRetention > 0
                        ? Math.Max(o.RunReport.GlobalHistoryRetention, options.Retention)
                        : 0,
                    PipelineIdentity = options.PipelineIdentity ?? o.RunReport.PipelineIdentity,
                }
                : o.RunReport with { HistoryRetention = 0 },
        }
        );

        builder.Services.RemoveAll<RocketHistoryOptions>();
        builder.Services.AddSingleton(options);
        // Always: ModularPipelines' own provider keeps the estimates in the user's application-data folder.
        builder.AddModuleEstimatedTimeProvider<RunHistoryEstimatedTimeProvider>();

        if (options.ModuleCache)
        {
            // Only the modules that declare cache inputs, key parts, environment variables or a version key are cached; the rest
            // always run. Cache input patterns are relative to the repository root.
            builder.AddModuleCache<FileSystemModuleCache>(o => o with
            {
                WorkingDirectory = options.RootDirectory,
                CacheDirectory = options.Resolve(options.ModuleCacheDirectory),
            }
            );
        }

        return builder;
    }
}
