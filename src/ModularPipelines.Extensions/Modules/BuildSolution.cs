using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

[DependsOn<RestoreSolution>]
[DependsOn<GitVersionModule>]
public partial class BuildSolution
(
    ArtifactSettings artifactSettings,
    SolutionSettings settings) : Module<BuildSolution.Result>
{
    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var binlog = artifactSettings.LogsDirectory.GetFile("build.binlog");
        var result = await context
                          .DotNet()
                          .Build(
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

    public record Result(CommandResult CommandResult, File Binlog);
}
