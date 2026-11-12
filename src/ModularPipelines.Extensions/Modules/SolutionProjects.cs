using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using ModularPipelines.Options;
using Proto.Promises;
using Proto.Promises.Threading;
using InvalidOperationException = System.InvalidOperationException;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>A project of the solution, as MSBuild evaluates it.</summary>
/// <param name="Path">The project file.</param>
/// <param name="SolutionPath">The path the solution lists it under.</param>
/// <param name="RootPath">The path relative to the root directory, which the test filters match.</param>
/// <param name="AssemblyName">The assembly it builds.</param>
/// <param name="IsTestProject">Whether it is a test project.</param>
public record SolutionProject(FilePath Path, string SolutionPath, string RootPath, string AssemblyName, bool IsTestProject);

internal static class SolutionProjects
{
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
        var solutionDirectory = System.IO.Path.GetDirectoryName(solution.Path)!;

        var projects = new ConcurrentBag<SolutionProject>();
        // Evaluation only reads the project files, so it does not need the throttling the test runs do.
        await ParallelAsync.ForEach(
            model.SolutionProjects.Select(project => project.FilePath),
            new ParallelAsyncOptions { CancelationToken = cancellationToken.ToCancelationToken(), MaxDegreeOfParallelism = -1 },
            async (solutionPath, token) =>
            {
                var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(solutionDirectory, solutionPath.Replace('\\', '/')));
                var properties = await GetPropertiesAsync(context, fullPath, null, token.ToCancellationToken());
                // A test framework package usually sets IsTestProject per target framework, so a multi-targeted project only says so
                // once one is chosen.
                if (!IsTrue(properties, "IsTestProject")
                 && properties.GetValueOrDefault("TargetFrameworks") is { Length: > 0 } frameworks)
                {
                    var first = frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                    properties = await GetPropertiesAsync(context, fullPath, first, token.ToCancellationToken());
                }

                projects.Add(
                    new(
                        new(fullPath),
                        solutionPath.Replace('\\', '/'),
                        System.IO.Path.GetRelativePath(sharedSettings.RootDirectory.Path, fullPath).Replace('\\', '/'),
                        properties.GetValueOrDefault("AssemblyName") is { Length: > 0 } assemblyName
                            ? assemblyName
                            : System.IO.Path.GetFileNameWithoutExtension(fullPath),
                        IsTrue(properties, "IsTestProject")
                    )
                );
            }
        );

        return [.. projects.OrderBy(project => project.RootPath, StringComparer.Ordinal)];
    }

    /// <summary>
    ///     Applies <see cref="TestSettings.IncludeTests" /> and <see cref="TestSettings.ExcludeTests" />, and warns about each pattern that
    ///     matches no project.
    /// </summary>
    public static bool IsSelected(SolutionProject project, TestSettings testSettings) =>
        ( testSettings.IncludeTests.Count == 0 || testSettings.IncludeTests.Any(pattern => Matches(pattern, project.RootPath)) )
     && !testSettings.ExcludeTests.Any(pattern => Matches(pattern, project.RootPath));

    public static void WarnAboutUnmatchedPatterns(IModuleContext context, IReadOnlyList<SolutionProject> projects, TestSettings testSettings)
    {
        foreach (var (key, pattern) in testSettings.IncludeTests.Select(pattern => ("IncludeTests", pattern))
                                                   .Concat(testSettings.ExcludeTests.Select(pattern => ("ExcludeTests", pattern)))
                                                   .Where(item => !projects.Any(project => Matches(item.pattern, project.RootPath))))
        {
            context.Logger.LogWarning("{Setting} pattern {Pattern} matches no project in the solution", key, pattern);
        }
    }

    /// <summary>
    ///     The default coverage settings with the module paths scoped to the solution's own non-test assemblies, plus
    ///     <see cref="TestSettings.CoverageInclude" />, never test assemblies or <see cref="TestSettings.CoverageExclude" />.
    /// </summary>
    public static async Task WriteDefaultRunSettingsAsync(FilePath target, IReadOnlyList<SolutionProject> projects, TestSettings testSettings, CancellationToken cancellationToken)
    {
        await using var resource = typeof(SolutionProjects)
                                  .Assembly
                                  .GetManifestResourceStream("Rocket.Surgery.ModularPipelines.Extensions.default.runsettings")!;
        var document = await XDocument.LoadAsync(resource, LoadOptions.PreserveWhitespace, cancellationToken);
        var modulePaths = document.Descendants("ModulePaths").Single();

        var include = projects
                     .Where(project => !project.IsTestProject)
                     .Select(project => project.AssemblyName)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal)
                     .Select(name => $@".*[\\/]{Regex.Escape(name)}\.dll$")
                     .Concat(testSettings.CoverageInclude)
                     .ToArray();
        var exclude = projects
                     .Where(project => project.IsTestProject)
                     .Select(project => project.AssemblyName)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal)
                     .Select(name => $@".*[\\/]{Regex.Escape(name)}\.dll$")
                     .Append(@".*\.Tests?\.dll$")
                     .Concat(testSettings.CoverageExclude)
                     .ToArray();

        // With nothing to include the collector instruments every module, which is the old behaviour.
        if (include.Length > 0) modulePaths.Add(new XElement("Include", include.Select(path => new XElement("ModulePath", path))));
        modulePaths.Add(new XElement("Exclude", exclude.Select(path => new XElement("ModulePath", path))));

        await using var stream = target.GetStream(FileAccess.Write);
        stream.SetLength(0);
        await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
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

        using var json = JsonDocument.Parse(result.StandardOutput);
        return json.RootElement.GetProperty("Properties")
                   .EnumerateObject()
                   .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);
    }
}
