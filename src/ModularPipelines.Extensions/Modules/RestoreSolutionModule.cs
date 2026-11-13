using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

public partial class RestoreSolutionModule
(
    SharedSettings sharedSettings,
    ArtifactSettings artifactSettings,
    SolutionSettings solutionSettings = null!) : Module<RestoreSolutionModule.Result>
{
    protected override async Task<Result> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var binlog = artifactSettings.LogsDirectory.GetFile("restore.binlog");
        if (sharedSettings.RootDirectory.GetFolder(".config").GetFile("dotnet-tools.json").Exists)
        {
            context.Logger.LogInformation("Restoring local tools...");
            await context.Tools.DotNet.Tool.RestoreAsync(new() { Interactive = context.Environment.BuildSystem.IsLocal }, new(), cancellationToken);
        }

        var result = await context
                          .Tools
                          .DotNet
                          .RestoreAsync(
                               new DotNetRestoreOptions
                               {
                                   ProjectSolution = solutionSettings.Solution,
                                   Interactive = context.Environment.BuildSystem.IsLocal,
                                   IgnoreFailedSources = true,
                               }.BinlogTo(context, binlog),
                               new(),
                               cancellationToken
                           );
        return new(result, binlog);
    }

    public record Result(CommandResult CommandResult, FilePath Binlog);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ToolsManifest))]
internal partial class ToolsManifestContext : JsonSerializerContext { }

internal class ToolsManifest
{
    public Dictionary<string, JsonElement> Tools { get; set; } = [with(StringComparer.OrdinalIgnoreCase)];
}
