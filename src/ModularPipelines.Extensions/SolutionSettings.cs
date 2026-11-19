using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class SolutionSettings(SharedSettings sharedSettings, IConfiguration configuration)
{
    public string Configuration => field ??= configuration.GetValue("Configuration", "Release");
    public FilePath Solution => field ??= sharedSettings.GetConfigurationFile(nameof(Solution))!;

    /// <summary>
    ///     How many projects <see cref="Modules.PackSolutionModule" /> packs at once, each in a <c>dotnet pack</c> of its own. Read from
    ///     <c>PackParallelism</c>; the processor count by default.
    /// </summary>
    public int PackParallelism => Math.Max(1, configuration.GetValue<int?>("PackParallelism") ?? Environment.ProcessorCount);
}
