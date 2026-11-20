using Microsoft.Extensions.DependencyInjection;

namespace Rocket.Surgery.ModularPipelines.Extensions.Readme;

/// <summary>The sections <see cref="Modules.ReadmeUpdaterModule" /> writes into the readme, and where the readme is.</summary>
/// <param name="sharedSettings">The settings shared by every module.</param>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class ReadmeSettings(SharedSettings sharedSettings)
{

    /// <summary>
    ///     The badges container
    /// </summary>
    public Badges Badges => field ??= new Badges()
           .Add(new GithubReleaseSection())
           .Add(new GithubLicenseSection())
           .Add(new CodecovSection())
           .Add(new CodacySection());

    /// <summary>
    ///     The history container
    /// </summary>
    public Histories History => field ??= new Histories()
           .Add(new AzurePipelinesHistory())
           .Add(new GitHubActionsHistory())
           .Add(new AppVeyorHistory());

    /// <summary>
    ///     The references container for markdown references
    /// </summary>
    public References References => field ??= new();

    /// <summary>
    ///     The sections container
    /// </summary>
    public Sections Sections => field ??= new Sections()
           .Add(Badges)
           .Add(History)
           .Add(References)
           //    .Add(new NugetPackagesSection())
           ;

    /// <summary>The readme: <c>README.md</c> in the root directory.</summary>
    public FilePath ReadmeFilePath => field ??= sharedSettings.RootDirectory.GetFile("README.md");

}
