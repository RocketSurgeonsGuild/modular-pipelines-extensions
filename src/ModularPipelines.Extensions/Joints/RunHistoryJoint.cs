using Joinery.Dovetail;
using Joinery.Dovetail.Attributes;
using Joinery.Dovetail.Joints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModularPipelines.Caching;
using ModularPipelines.Options;
using Rocket.Surgery.ModularPipelines.Extensions.History;

namespace Rocket.Surgery.ModularPipelines.Extensions.Joints;

/// <summary>
///     Keeps everything ModularPipelines remembers between runs under <c>.modularpipelines/</c> in the repository, where a CI cache can
///     restore it, instead of the user's application-data folders, as the <c>History</c> section (<see cref="RocketHistoryOptions" />)
///     says:
///     <list type="bullet">
///         <item>run history (<c>run-history</c>), grouped under a stable pipeline identity;</item>
///         <item>
///             duration estimates (<c>estimates</c>): the median of a module's last successful runs, or else its last duration, which
///             order the critical path, the progress display and the test projects;
///         </item>
///         <item>the module cache (<c>module-cache</c>), which restores the modules that declare their cache inputs.</item>
///     </list>
/// </summary>
[DovetailExport]
public sealed class RunHistoryJoint : IPipelineBuilderJoint
{
    /// <inheritdoc />
    public void Register(IDovetailContext context, PipelineBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // The run report options are applied once the services are built, from IOptions<RocketHistoryOptions>.
        builder.Services.AddSingleton<IConfigureOptions<PipelineOptions>, RunHistoryPipelineOptions>();

        // Always: ModularPipelines' own provider keeps the estimates in the user's application-data folder.
        builder.AddModuleEstimatedTimeProvider<RunHistoryEstimatedTimeProvider>();

        // The module cache is a registration, not an option, so it is decided while the builder is set up. The configuration binding
        // generator makes this Get reflection free.
        var options = builder.Configuration.GetSection("History").Get<RocketHistoryOptions>() ?? new();
        if (options.ModuleCache.Enabled)
        {
            var root = new SharedSettings(builder.Configuration).RootDirectory.Path;
            // Only the modules that declare cache inputs, key parts, environment variables or a version key are cached; the rest
            // always run. Cache input patterns are relative to the repository root.
            builder.AddModuleCache<FileSystemModuleCache>(o => o with
            {
                WorkingDirectory = root,
                CacheDirectory = Path.GetFullPath(options.ModuleCache.Directory, root),
            }
            );
        }
    }
}
