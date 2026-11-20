
namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>Builds the solution with <c>dotnet build</c>, passing the GitVersion properties and writing a binary log.</summary>
/// <param name="artifactSettings">Where the binary log goes.</param>
/// <param name="settings">The solution and configuration to build.</param>
[DependsOn<DownloadPackageIconModule>(Optional = true)]
[DependsOn<RestoreSolutionModule>]
[DependsOn<GitVersionModule>]
public partial class BuildSolution
(
    ArtifactSettings artifactSettings,
    SolutionSettings settings) : Module<BuildSolution.Result>
{
    /// <inheritdoc />
    protected override async Task<Result> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var binlog = artifactSettings.LogsDirectory.GetFile("build.binlog");
        var result = await context
                          .Tools
                          .DotNet
                          .BuildAsync(
                               new DotNetBuildOptions
                               {
                                   ProjectSolution = settings.Solution,
                                   Configuration = settings.Configuration,
                                   // Interactive = context.IsRunningLocally(),
                                   NoRestore = true,
                                   // TODO: Add verbosity, and loggers.
                                   // Arguments
                                   Properties = ( await context.GetModule<GitVersionModule>() ).ValueOrDefault?.Properties,
                               }.BinlogTo(context, binlog),
                               new(),
                               cancellationToken
                           );
        return new(result, binlog);
    }

    /// <summary>The outcome of the build.</summary>
    /// <param name="CommandResult">The result of <c>dotnet build</c>.</param>
    /// <param name="Binlog">The binary log location.</param>
    public record Result(CommandResult CommandResult, FilePath Binlog);
}
