using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rocket.Surgery.ModularPipelines.Extensions;

/// <summary>The solution the pipeline builds, and how.</summary>
/// <param name="sharedSettings">The settings shared by every module.</param>
/// <param name="configuration">The pipeline configuration.</param>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class SolutionSettings(SharedSettings sharedSettings, IConfiguration configuration)
{
    /// <summary>The build configuration. Read from <c>Configuration</c>; <c>Release</c> by default.</summary>
    public string Configuration => field ??= configuration.GetValue("Configuration", "Release");
    /// <summary>The solution file. Read from <c>Solution</c>, relative to the root directory.</summary>
    public FilePath Solution => field ??= sharedSettings.GetConfigurationFile(nameof(Solution))!;

    /// <summary>
    ///     How many projects <see cref="Modules.PackSolutionModule" /> packs at once, each in a <c>dotnet pack</c> of its own. Read from
    ///     <c>PackParallelism</c>; the processor count by default.
    /// </summary>
    public int PackParallelism => Math.Max(1, configuration.GetValue<int?>("PackParallelism") ?? Environment.ProcessorCount);
}
