using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using File = ModularPipelines.FileSystem.File;

namespace build.library;

public partial class RestoreSolution
(
    SharedSettings sharedSettings,
    ArtifactSettings artifactSettings,
    SolutionSettings solutionSettings = null!) : Module<RestoreSolution.Result>
{
    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var binlog = artifactSettings.LogsDirectory.GetFile("restore.binlog");
        if (sharedSettings.RootDirectory.GetFolder(".config").GetFile("dotnet-tools.json").Exists)
        {
            context.Logger.LogInformation("Restoring local tools...");
            await context.DotNet().Tool.Restore(new() { Interactive = context.IsRunningLocally() }, new(), cancellationToken);
        }

        var result = await context
                          .DotNet()
                          .Restore(
                               new DotNetRestoreOptions
                               {
                                   ProjectSolution = solutionSettings.Solution,
                                   Interactive = context.IsRunningLocally(),
                                   IgnoreFailedSources = true,
                               }.BinlogTo(context, binlog),
                               new(),
                               cancellationToken
                           );
        return new(result, binlog);
    }

    public record Result(CommandResult CommandResult, File Binlog);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ToolsManifest))]
internal partial class ToolsManifestContext : JsonSerializerContext { }

internal class ToolsManifest
{
    public Dictionary<string, JsonElement> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
