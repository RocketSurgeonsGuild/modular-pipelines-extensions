using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class SolutionSettings(SharedSettings sharedSettings, IConfiguration configuration)
{
    public string Configuration => field ??= configuration.GetValue("Configuration", "Release");
    public File Solution => field ??= sharedSettings.GetConfigurationFile(nameof(Solution))!;
}
