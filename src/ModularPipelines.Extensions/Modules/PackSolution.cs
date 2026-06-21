using System.Collections.Immutable;
using File = ModularPipelines.FileSystem.File;

namespace build.library.Modules;

[DependsOn<BuildSolution>]
[DependsOn<GitVersionModule>]
public partial class PackSolution
(
    ArtifactSettings artifactSettings,
    SolutionSettings settings) : Module<PackSolution.Result>
{
    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var nugetFolder = artifactSettings.ArtifactsDirectory.GetFolder("nuget");
        var result = await context
                          .DotNet()
                          .Pack(
                               new()
                               {
                                   ProjectSolution = settings.Solution,
                                   Configuration = settings.Configuration,
                                   NoRestore = true,
                                   NoBuild = true,
                                   IncludeSource = true,
                                   IncludeSymbols = true,
                                   Output = nugetFolder,
                                   Properties = ( await context.GetModule<GitVersionModule>() ).ValueOrDefault?.Properties,
                               },
                               new(),
                               cancellationToken
                           );

        var packages = nugetFolder.GetFiles(z => z.Extension == "nupkg").ToImmutableList();
        var symbols = nugetFolder.GetFiles(z => z.Extension == "snupkg").ToImmutableList();

        return new(result, packages, symbols);
    }

    public record Result(CommandResult CommandResult, ImmutableList<File> Packages, ImmutableList<File> Symbols);
}
