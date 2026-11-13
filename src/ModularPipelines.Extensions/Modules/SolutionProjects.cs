using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using ModularPipelines.Options;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>A project of the solution, as MSBuild evaluates it.</summary>
/// <param name="Path">The project file.</param>
/// <param name="SolutionPath">The path the solution lists it under.</param>
/// <param name="RootPath">The path relative to the root directory, which <see cref="TestSettings.ExcludeTests" /> matches.</param>
/// <param name="AssemblyName">The assembly it builds.</param>
/// <param name="IsTestProject">Whether it is a test project.</param>
public record SolutionProject(FilePath Path, string SolutionPath, string RootPath, string AssemblyName, bool IsTestProject);

internal static class SolutionProjects
{
    /// <summary>Lists the projects of the solution, asking MSBuild for each one's assembly name and whether it is a test project.</summary>
    public static async Task<IReadOnlyList<SolutionProject>> EvaluateAsync(
        IModuleContext context,
        SharedSettings sharedSettings,
        FilePath solution,
        CancellationToken cancellationToken
    )
    {
        var serializer = SolutionSerializers.GetSerializerByMoniker(solution.Path)
         ?? throw new InvalidOperationException($"Not a solution file: {solution.Path}");
        var model = await serializer.OpenAsync(solution.Path, cancellationToken);
        var solutionDirectory = Path.GetDirectoryName(solution.Path)!;

        var projects = new ConcurrentBag<SolutionProject>();
        // Evaluation only reads the project files, so it is not throttled like the test runs are.
        await Parallel.ForEachAsync(
            model.SolutionProjects.Select(project => project.FilePath),
            cancellationToken,
            async (solutionPath, token) =>
            {
                var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, solutionPath.Replace('\\', '/')));
                var properties = await GetPropertiesAsync(context, fullPath, null, token);
                // A test framework package usually sets IsTestProject per target framework, so a multi-targeted project only says so
                // once one is chosen.
                if (!IsTrue(properties, "IsTestProject")
                 && properties.GetValueOrDefault("TargetFrameworks") is { Length: > 0 } frameworks)
                {
                    var first = frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                    properties = await GetPropertiesAsync(context, fullPath, first, token);
                }

                projects.Add(
                    new(
                        new(fullPath),
                        solutionPath.Replace('\\', '/'),
                        Path.GetRelativePath(sharedSettings.RootDirectory.Path, fullPath).Replace('\\', '/'),
                        properties.GetValueOrDefault("AssemblyName") is { Length: > 0 } assemblyName
                            ? assemblyName
                            : Path.GetFileNameWithoutExtension(fullPath),
                        IsTrue(properties, "IsTestProject")
                    )
                );
            }
        );

        return [.. projects.OrderBy(project => project.RootPath, StringComparer.Ordinal)];
    }

    /// <summary>Whether <see cref="TestSettings.ExcludeTests" /> leaves the project out of the test run.</summary>
    public static bool IsExcluded(SolutionProject project, TestSettings testSettings) =>
        testSettings.ExcludeTests.Any(pattern => Matches(pattern, project.RootPath));

    /// <summary>Warns about each <see cref="TestSettings.ExcludeTests" /> pattern that matches no project.</summary>
    public static void WarnAboutUnmatchedPatterns(IModuleContext context, IReadOnlyList<SolutionProject> projects, TestSettings testSettings)
    {
        foreach (var pattern in testSettings.ExcludeTests.Where(pattern => !projects.Any(project => Matches(pattern, project.RootPath))))
        {
            context.Logger.LogWarning("ExcludeTests pattern {Pattern} matches no project in the solution", pattern);
        }
    }

    public static bool Matches(string pattern, string path)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern);
        return matcher.Match(path).HasMatches;
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
