using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class SolutionSettings(SharedSettings sharedSettings, IConfiguration configuration)
{
    public string Configuration => field ??= configuration.GetValue("Configuration", "Release");
    public FilePath Solution => field ??= sharedSettings.GetConfigurationFile(nameof(Solution))!;

    /// <summary>
    ///     Extra arguments for the solution build, such as <c>-maxCpuCount:8</c> or <c>-graphBuild</c>. Read from the <c>BuildArguments</c>
    ///     list; none by default.
    /// </summary>
    public IReadOnlyList<string> BuildArguments =>
        field ??= [.. configuration.GetSection(nameof(BuildArguments)).GetChildren().Select(z => z.Value).OfType<string>().Where(z => z.Length > 0)];
}
