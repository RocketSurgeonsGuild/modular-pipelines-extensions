using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.FileSystem;

namespace Rocket.Surgery.ModularPipelines.Extensions;


[ServiceRegistration(ServiceLifetime.Singleton)]
public class DocsSettings(SharedSettings sharedSettings, IConfiguration configuration)
{
    public bool DocsEnabled { get; } = configuration.GetValue("Docs:Enabled", true);
    public Folder DocsDirectory => field ??= configuration.GetValue<Folder?>("Docs:Directory") ?? sharedSettings.RootDirectory / "docs";
    public string DocsBuildTask => field ??= configuration.GetValue<string>("Docs:Task") ?? "docs:build";
    public Folder DocsOutputDirectory => field ??= configuration.GetValue<Folder?>("Docs:Output") ?? DocsDirectory / "dist";
}
