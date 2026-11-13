using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using ModularPipelines.Exceptions;
using ModularPipelines.Options;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Lists the projects of the solution, asking MSBuild for each one's assembly name, target frameworks and whether it is a test project.
///     Depends on <see cref="RestoreSolutionModule" />, since a project's packages (including its test SDK, which sets
///     <see cref="Project.IsTestProject" />) must be restored before MSBuild can evaluate it.
/// </summary>
/// <remarks>
///     <para>
///         Every project is evaluated in one MSBuild process: a generated project calls a small target in each of them (imported through
///         <c>CustomAfterMicrosoftCommonTargets</c> and <c>CustomAfterMicrosoftCommonCrossTargetingTargets</c>), and a second pass asks
///         the multi-targeted ones that did not say whether they are a test project with their first target framework chosen, since a
///         test framework package usually sets <c>IsTestProject</c> per target framework. One process per project, as before, spent a
///         minute or more of processor time beside the solution build on a 4-core CI runner.
///     </para>
///     <para>
///         Only <see cref="TestSolutionModule" /> in <see cref="TestMode.PerProject" /> mode reads the list, so the module is skipped when
///         tests are disabled or run over the solution.
///     </para>
/// </remarks>
[DependsOn<RestoreSolutionModule>]
public partial class SolutionProjectsModule(SharedSettings sharedSettings, TestSettings testSettings, SolutionSettings settings = null!)
    : Module<ImmutableList<SolutionProjectsModule.Project>>
{
    private const int Attempts = 2;
    private const string InfoTarget = "_RocketSurgeryProjectInfo";
    private const string CollectTarget = "Collect";

    /// <summary>A project of the solution, as MSBuild evaluates it.</summary>
    /// <param name="ProjectPath">The path the solution lists it under.</param>
    /// <param name="AssemblyName">The assembly it builds.</param>
    /// <param name="IsTestProject">Whether it is a test project.</param>
    public record Project(FilePath ProjectPath, string AssemblyName, bool IsTestProject);

    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(_ => settings is null
        ? SkipDecision.Skip("No solution was provided")
        : SkipDecision.When(
            !testSettings.IsEnabled || testSettings.Mode != TestMode.PerProject,
            "Only the per-project test run reads the solution's projects"
        )
    );

    protected override async Task<ImmutableList<Project>> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var solution = settings.Solution;
        var serializer = SolutionSerializers.GetSerializerByMoniker(solution)
         ?? throw new InvalidOperationException($"Not a solution file: {solution}");
        var model = await serializer.OpenAsync(solution, cancellationToken);
        var solutionDirectory = solution.Folder!;
        var projectPaths = model.SolutionProjects.Select(project => new FilePath(Path.GetFullPath(solutionDirectory.GetFile(project.FilePath).Path))).ToArray();

        var workDirectory = ( sharedSettings.TempDirectory / "solution-projects" ).EnsureExists();
        var infoTargets = workDirectory.GetFile("project-info.targets");
        await File.WriteAllTextAsync(infoTargets.Path, InfoTargetsContent, cancellationToken);

        var outer = await EvaluateAsync(context, workDirectory.GetFile("outer.proj"), infoTargets, projectPaths.Select(path => (path, (string?)null)), cancellationToken);
        var inner = await EvaluateAsync(
            context,
            workDirectory.GetFile("inner.proj"),
            infoTargets,
            outer
               .Where(pair => !IsTrue(pair.Value.IsTestProject) && pair.Value.FirstTargetFramework is { })
               .Select(pair => (new FilePath(pair.Key), pair.Value.FirstTargetFramework)),
            cancellationToken
        );

        return
        [
            .. projectPaths
              .Select(path =>
                   {
                       var info = inner.GetValueOrDefault(path.Path) ?? outer.GetValueOrDefault(path.Path);
                       return new Project(
                           path,
                           info?.AssemblyName is { Length: > 0 } assemblyName ? assemblyName : path.NameWithoutExtension,
                           IsTrue(info?.IsTestProject)
                       );
                   }
               )
              .OrderBy(project => project.ProjectPath.Path, StringComparer.Ordinal),
        ];
    }

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Evaluates the projects, each with its target framework when one is given, in one MSBuild process.</summary>
    /// <returns>What each project said, by its full path; a project without the common targets (no SDK) is left out.</returns>
    private async Task<Dictionary<string, ProjectInfo>> EvaluateAsync(
        IModuleContext context,
        FilePath collector,
        FilePath infoTargets,
        IEnumerable<(FilePath Path, string? TargetFramework)> projects,
        CancellationToken cancellationToken
    )
    {
        var list = projects.ToArray();
        if (list.Length == 0) return [with(StringComparer.OrdinalIgnoreCase)];

        var targets = Escape(infoTargets.Path);
        var document = new XDocument(
            new XElement(
                "Project",
                new XElement(
                    "ItemGroup",
                    list.Select(project => new XElement(
                                    "_RocketSurgeryProject",
                                    new XAttribute("Include", Escape(project.Path.Path)),
                                    project.TargetFramework is { } framework ? new XAttribute("AdditionalProperties", $"TargetFramework={Escape(framework)}") : null
                                )
                    )
                ),
                new XElement(
                    "Target",
                    new XAttribute("Name", CollectTarget),
                    new XAttribute("Returns", "@(_RocketSurgeryProjectInfo)"),
                    new XElement(
                        "MSBuild",
                        new XAttribute("Projects", "@(_RocketSurgeryProject)"),
                        new XAttribute("Targets", InfoTarget),
                        new XAttribute("BuildInParallel", "true"),
                        // A project that does not import the common targets (no SDK) has no info target, and is not a test project.
                        new XAttribute("SkipNonexistentTargets", "true"),
                        new XAttribute("Properties", $"CustomAfterMicrosoftCommonTargets={targets};CustomAfterMicrosoftCommonCrossTargetingTargets={targets}"),
                        new XElement("Output", new XAttribute("TaskParameter", "TargetOutputs"), new XAttribute("ItemName", "_RocketSurgeryProjectInfo"))
                    )
                )
            )
        );
        await File.WriteAllTextAsync(collector.Path, document.ToString(), cancellationToken);

        // Evaluating alongside the build now and then fails on a locked file; one more attempt gets past it, and the output of a failure
        // is logged, since the command itself runs silently.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await context.Tools.DotNet.MsbuildAsync(
                    new() { Arguments = [collector.Path, $"-getTargetResult:{CollectTarget}", "-maxCpuCount", "-nodeReuse:false", "-nologo"] },
                    new() { Logging = CommandLoggingOptions.Silent },
                    cancellationToken
                );
                return Parse(result.StandardOutput);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var output = ex is CommandException { Result: { } failed }
                    ? string.Join(Environment.NewLine, new[] { failed.StandardOutput, failed.StandardError }.Where(z => !string.IsNullOrWhiteSpace(z)))
                    : ex.Message;
                if (attempt >= Attempts)
                {
                    LogEvaluationFailed(context.Logger, list.Length, attempt, output);
                    throw;
                }

                LogEvaluationRetrying(context.Logger, list.Length, output);
            }
        }
    }

    /// <summary>Reads the items <c>-getTargetResult</c> prints as JSON.</summary>
    internal static Dictionary<string, ProjectInfo> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var results = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);
        if (!document.RootElement.TryGetProperty("TargetResults", out var targetResults)
         || !targetResults.TryGetProperty(CollectTarget, out var collect)
         || !collect.TryGetProperty("Items", out var items))
        {
            return results;
        }

        foreach (var item in items.EnumerateArray())
        {
            var path = Get(item, "Identity");
            if (path is not { Length: > 0 }) continue;

            var frameworks = Get(item, "TargetFrameworks");
            results[Path.GetFullPath(path)] = new(
                Get(item, "AssemblyName"),
                Get(item, "IsTestProject"),
                // Only the outer build of a multi-targeted project lists its frameworks without one chosen.
                Get(item, "TargetFramework") is { Length: > 0 } || frameworks is not { Length: > 0 }
                    ? null
                    : frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            );
        }

        return results;

        static string? Get(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    /// <summary>Escapes the characters MSBuild treats as special in an item or property value.</summary>
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '%' or '$' or '@' or '\'' or ';' or '?' or '*')
            {
                builder.Append('%').Append(( (int)character ).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private const string InfoTargetsContent = $"""
        <Project>
          <Target Name="{InfoTarget}" Returns="@(_RocketSurgeryProjectInfo)">
            <ItemGroup>
              <_RocketSurgeryProjectInfo Include="$(MSBuildProjectFullPath)"
                                         AssemblyName="$(AssemblyName)"
                                         IsTestProject="$(IsTestProject)"
                                         TargetFramework="$(TargetFramework)"
                                         TargetFrameworks="$(TargetFrameworks)" />
            </ItemGroup>
          </Target>
        </Project>
        """;

    /// <summary>What MSBuild said about a project.</summary>
    /// <param name="AssemblyName">Its assembly name.</param>
    /// <param name="IsTestProject">Its <c>IsTestProject</c> property.</param>
    /// <param name="FirstTargetFramework">The first of its target frameworks when it is multi-targeted and none was chosen.</param>
    internal record ProjectInfo(string? AssemblyName, string? IsTestProject, string? FirstTargetFramework);

    [LoggerMessage(LogLevel.Warning, "Evaluating {Count} projects failed, trying once more:\n{Output}")]
    private static partial void LogEvaluationRetrying(ILogger logger, int count, string output);

    [LoggerMessage(LogLevel.Error, "Evaluating {Count} projects failed after {Attempts} attempts:\n{Output}")]
    private static partial void LogEvaluationFailed(ILogger logger, int count, int attempts, string output);
}
