using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.DependencyInjection;
using Rocket.Surgery.ModularPipelines.Extensions.Readme;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class NuGetSettings(IConfiguration configuration)
{
    public string? NuGetApiKey => configuration.GetValue<string?>("RSG_NUGET_API_KEY")
                                  ?? configuration.GetValue<string?>("NUGET_API_KEY")
                                  ?? Environment.GetEnvironmentVariable("RSG_NUGET_API_KEY")
                                  ?? Environment.GetEnvironmentVariable("NUGET_API_KEY");
}
