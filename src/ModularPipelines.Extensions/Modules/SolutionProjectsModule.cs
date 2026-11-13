using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using ModularPipelines.Options;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Lists the projects of the solution, asking MSBuild for each one's assembly name and whether it is a test project. Depends on
///     <see cref="RestoreSolutionModule" />, since a project's packages (including its test SDK, which sets <see cref="SolutionProject.IsTestProject" />)
///     must be restored before MSBuild can evaluate it.
/// </summary>
[DependsOn<RestoreSolutionModule>]
public class SolutionProjectsModule(SolutionSettings settings = null!) : Module<ImmutableList<SolutionProjectsModule.Project>>
{

    /// <summary>A project of the solution, as MSBuild evaluates it.</summary>
    /// <param name="ProjectPath">The path the solution lists it under.</param>
    /// <param name="AssemblyName">The assembly it builds.</param>
    /// <param name="IsTestProject">Whether it is a test project.</param>
    public record Project(FilePath ProjectPath, string AssemblyName, bool IsTestProject);

    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(_ => SkipDecision.When(
                                                                                                    settings is null,
                                                                                                    "No solution was provided"
                                                                                                )
    );

    protected override async Task<ImmutableList<Project>> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var solution = settings.Solution;
        var serializer = SolutionSerializers.GetSerializerByMoniker(solution)
         ?? throw new InvalidOperationException($"Not a solution file: {solution}");
        var model = await serializer.OpenAsync(solution, cancellationToken);
        var solutionDirectory = solution.Folder!;

        var projects = new ConcurrentBag<Project>();
        // Evaluation only reads the project files, so it is not throttled like the test runs are.
        await Parallel.ForEachAsync(
            model.SolutionProjects.Select(project => solutionDirectory.GetFile(project.FilePath)),
            cancellationToken,
            async (projectPath, token) =>
            {
                var properties = await GetPropertiesAsync(context, projectPath, null, token);
                // A test framework package usually sets IsTestProject per target framework, so a multi-targeted project only says so
                // once one is chosen.
                if (!IsTrue(properties, "IsTestProject")
                 && properties.GetValueOrDefault("TargetFrameworks") is { Length: > 0 } frameworks)
                {
                    var first = frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                    properties = await GetPropertiesAsync(context, projectPath, first, token);
                }

                projects.Add(
                    new(
                        projectPath,
                        properties.GetValueOrDefault("AssemblyName") is { Length: > 0 } assemblyName
                            ? assemblyName
                            : projectPath.NameWithoutExtension,
                        IsTrue(properties, "IsTestProject")
                    )
                );
            }
        );

        return [.. projects.OrderBy(project => project.ProjectPath.Path, StringComparer.Ordinal)];
    }

    private static bool IsTrue(Dictionary<string, string> properties, string name) =>
        string.Equals(properties.GetValueOrDefault(name), "true", StringComparison.OrdinalIgnoreCase);

    private static async Task<Dictionary<string, string>> GetPropertiesAsync(
        IModuleContext context,
        string project,
        string? targetFramework,
        CancellationToken cancellationToken
    )
    {
        List<string> arguments =
        [
            project,
            "-getProperty:IsTestProject",
            "-getProperty:AssemblyName",
            "-getProperty:TargetFrameworks",
        ];
        if (targetFramework is not null) arguments.Add($"-property:TargetFramework={targetFramework}");

        var result = await context.Tools.DotNet.MsbuildAsync(
            new() { Arguments = arguments },
            new() { Logging = CommandLoggingOptions.Silent },
            cancellationToken
        );

        // With more than one -getProperty, MSBuild prints them as JSON.
        using var json = JsonDocument.Parse(result.StandardOutput);
        return json.RootElement.GetProperty("Properties")
                   .EnumerateObject()
                   .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);
    }
}
