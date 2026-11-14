
namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[DependsOn<DownloadPackageIconModule>(Optional = true)]
[DependsOn<RestoreSolutionModule>]
[DependsOn<GitVersionModule>]
public partial class BuildSolution
(
    ArtifactSettings artifactSettings,
    SolutionSettings settings) : Module<BuildSolution.Result>
{
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
                                   // Ahead of BinlogTo's own arguments, which it appends.
                                   Arguments = [.. settings.BuildArguments],
                                   Properties = ( await context.GetModule<GitVersionModule>() ).ValueOrDefault?.Properties,
                               }.BinlogTo(context, binlog),
                               new(),
                               cancellationToken
                           );
        return new(result, binlog);
    }

    public record Result(CommandResult CommandResult, FilePath Binlog);
}
