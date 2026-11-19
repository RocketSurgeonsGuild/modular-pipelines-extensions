using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using Humanizer;
using Microsoft.Extensions.Logging;
using ModularPipelines.Exceptions;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Packs the solution's packable projects into the <c>nuget</c> artifacts folder: one <c>dotnet pack --no-build</c> per project, each a
///     sub module, <see cref="SolutionSettings.PackParallelism" /> at once.
/// </summary>
/// <remarks>
///     One <c>dotnet pack</c> over the solution packed every project in one MSBuild process. With an SBOM generated for each package
///     (Microsoft.Sbom.Targets) that process now and then stalled on one project after its package was written, and the module ran until
///     its timeout without saying which one. A process per project keeps one project's trouble to itself, and the sub module names it.
/// </remarks>
[DependsOn<BuildSolution>]
[DependsOn<GitVersionModule>]
[DependsOn<SolutionProjectsModule>]
public partial class PackSolutionModule
(
    ArtifactSettings artifactSettings,
    SolutionSettings settings) : Module<PackSolutionModule.Result>
{
    /// <inheritdoc />
    [SuppressMessage("ModularPipelines", "MP0016", Justification = "Each call passes the module's token on. MP0016 only recognizes ExecuteAsync's own.")]
    protected override async Task<Result> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var nugetFolder = artifactSettings.ArtifactsDirectory.GetFolder("nuget").EnsureExists();
        var properties = ( await context.GetModule<GitVersionModule>() ).ValueOrDefault?.Properties;
        var projects = Packable(( await context.GetModule<SolutionProjectsModule>() ).ValueOrDefault ?? []);
        if (projects.Count == 0)
        {
            LogNothingToPack(context.Logger);
        }
        else if (context.Logger.IsEnabled(LogLevel.Information))
        {
            LogPacking(context.Logger, projects.Count, settings.PackParallelism);
        }

        var outcomes = new ConcurrentBag<ProjectResult>();
        await Parallel.ForEachAsync(
            projects,
            new ParallelOptions { MaxDegreeOfParallelism = settings.PackParallelism, CancellationToken = cancellationToken },
            async (project, token) => outcomes.Add(await PackProjectAsync(context, project, nugetFolder, properties, token))
        );

        var packages = nugetFolder.GetFiles(z => z.Extension == ".nupkg").ToImmutableList();
        var symbols = nugetFolder.GetFiles(z => z.Extension == ".snupkg").ToImmutableList();
        return ThrowIfAnyFailed(new(packages, symbols, [.. outcomes.OrderBy(outcome => outcome.Name, StringComparer.Ordinal)]));
    }

    /// <summary>The projects <c>dotnet pack</c> makes a package of, in path order.</summary>
    internal static ImmutableList<SolutionProjectsModule.Project> Packable(IEnumerable<SolutionProjectsModule.Project> projects) =>
        [.. projects.Where(project => project is { IsPackable: true, IsTestProject: false }).OrderBy(project => project.ProjectPath.Path, StringComparer.Ordinal)];

    [SuppressMessage(
        "ModularPipelines",
        "MP0016",
        Justification = "Each call passes a token: the one Parallel.ForEachAsync gives, which ends with the module's. MP0016 only recognizes ExecuteAsync's own."
    )]
    private async Task<ProjectResult> PackProjectAsync(
        IModuleContext context,
        SolutionProjectsModule.Project project,
        FolderPath nugetFolder,
        IReadOnlyList<KeyValue>? properties,
        CancellationToken cancellationToken
    )
    {
        var name = project.ProjectPath.NameWithoutExtension;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var commandResult = await context.RunSubModuleAsync(
                name,
                token => context.Tools.DotNet.PackAsync(
                    new()
                    {
                        ProjectSolution = project.ProjectPath.Path,
                        Configuration = settings.Configuration,
                        NoRestore = true,
                        NoBuild = true,
                        Output = nugetFolder,
                        Properties = properties,
                        // Its own MSBuild nodes, which end with it: several packs run at once.
                        Arguments = ["-nodeReuse:false"],
                    },
                    new(),
                    token
                ),
                cancellationToken
            );
            LogPacked(context.Logger, name, Stopwatch.GetElapsedTime(started).Humanize(2));
            return new(name, project.ProjectPath, commandResult, null);
        }
        // One failing project must not stop the others; the module fails once they all finished.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogPackFailed(context.Logger, name, Stopwatch.GetElapsedTime(started).Humanize(2));
            return new(name, project.ProjectPath, ( ex as CommandException )?.Result, ex.Message.Split('\n', 2)[0].TrimEnd());
        }
    }

    /// <summary>Fails the module, listing every project that failed to pack, once all of them finished.</summary>
    private static Result ThrowIfAnyFailed(Result result) =>
        result.Projects.Where(project => project.Error is not null).ToArray() is { Length: > 0 } failed
            ? throw new InvalidOperationException(
                $"Packing failed in {failed.Length} of {result.Projects.Count} projects:{Environment.NewLine}"
              + string.Join(Environment.NewLine, failed.Select(project => $"  {project.Name}: {project.Error}"))
            )
            : result;

    /// <summary>The packages made.</summary>
    /// <param name="Packages">The packages in the <c>nuget</c> artifacts folder.</param>
    /// <param name="Symbols">The symbol packages in the <c>nuget</c> artifacts folder.</param>
    /// <param name="Projects">The outcome of each project's <c>dotnet pack</c>.</param>
    public record Result(ImmutableList<FilePath> Packages, ImmutableList<FilePath> Symbols, ImmutableList<ProjectResult> Projects);

    /// <summary>The outcome of one project's <c>dotnet pack</c>.</summary>
    /// <param name="Name">The sub module's name: the project's file name.</param>
    /// <param name="Project">The project packed.</param>
    /// <param name="CommandResult">The command's result, when it ran.</param>
    /// <param name="Error">Why it failed, or <see langword="null" />.</param>
    public record ProjectResult(string Name, FilePath Project, CommandResult? CommandResult, string? Error);

    [LoggerMessage(LogLevel.Information, "Packing {Count} projects, {Parallelism} at once")]
    static partial void LogPacking(ILogger logger, int count, int parallelism);

    [LoggerMessage(LogLevel.Warning, "The solution has no packable project; nothing to pack")]
    static partial void LogNothingToPack(ILogger logger);

    [LoggerMessage(LogLevel.Information, "■ {Name} packed in {Duration}")]
    static partial void LogPacked(ILogger logger, string name, string duration);

    [LoggerMessage(LogLevel.Warning, "■ {Name} failed to pack after {Duration}")]
    static partial void LogPackFailed(ILogger logger, string name, string duration);
}
