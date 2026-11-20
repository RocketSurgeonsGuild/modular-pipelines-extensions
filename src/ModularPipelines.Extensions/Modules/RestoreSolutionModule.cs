using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>Restores the solution with <c>dotnet restore</c>, writing a binary log.</summary>
/// <param name="sharedSettings">The settings shared by every module.</param>
/// <param name="artifactSettings">Where the binary log goes.</param>
/// <param name="solutionSettings">The solution to restore.</param>
public partial class RestoreSolutionModule
(
    SharedSettings sharedSettings,
    ArtifactSettings artifactSettings,
    SolutionSettings solutionSettings = null!) : Module<RestoreSolutionModule.Result>
{
    /// <inheritdoc />
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

    /// <summary>The outcome of the restore.</summary>
    /// <param name="CommandResult">The result of <c>dotnet restore</c>.</param>
    /// <param name="Binlog">The binary log location.</param>
    public record Result(CommandResult CommandResult, FilePath Binlog);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ToolsManifest))]
internal partial class ToolsManifestContext : JsonSerializerContext { }

internal class ToolsManifest
{
    public Dictionary<string, JsonElement> Tools { get; set; } = [with(StringComparer.OrdinalIgnoreCase)];
}
