using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using ModularPipelines.Options;

namespace build.library.Modules;

public partial class RemoveUnusedDependenciesModule(SharedSettings sharedSettings) : Module<RemoveUnusedDependenciesModule.Result>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration
                                                         .Create()
                                                         .WithSkipWhen(() => SkipDecision.Of(
                                                             IsOnCI(),
                                                             "Only runs on local builds"
                                                         ))
                                                         .Build();

    private static bool IsOnCI() =>
        Environment.GetEnvironmentVariable("CI") is not null ||
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null;

    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        // Get git-tracked project files
        var trackedFilesResult = await context.GetService<ICommand>().ExecuteCommandLineTool(
            new GitLsFilesOptions
            {
                Tool = "git",
                CommandParts = ["ls-files"],
                RunSettings = ["*.csproj", "*.props", "*.targets"],
            },
            new() { LogSettings = CommandLoggingOptions.Silent },
            cancellationToken
        );

        var projectFiles = trackedFilesResult.StandardOutput
                                             .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                             .Select(f => Path.Combine(sharedSettings.RootDirectory.Path, f.Trim()))
                                             .Where(File.Exists)
                                             .ToList();

        // Load all documents and collect all PackageReference names across the whole repo
        var documents = projectFiles
                       .Select(f => (Path: f, Doc: XDocument.Load(f, LoadOptions.PreserveWhitespace)))
                       .ToList();

        var allPackageReferences = documents
                                  .SelectMany(d => d.Doc.Descendants("PackageReference").Concat(d.Doc.Descendants("GlobalPackageReference")))
                                  .Select(e => e.Attribute("Include")?.Value)
                                  .Where(v => v is not null)
                                  .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        var globalPackageReferences = documents
                                     .SelectMany(d => d.Doc.Descendants("GlobalPackageReference"))
                                     .Select(e => e.Attribute("Include")?.Value)
                                     .Where(v => v is not null)
                                     .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        var removed = 0;
        foreach (var (path, doc) in documents)
        {
            var modified = false;

            // Remove PackageVersion entries that aren't referenced anywhere
            foreach (var pv in doc.Descendants("PackageVersion").ToList())
            {
                var name = pv.Attribute("Include")?.Value;
                if (name is null) continue;
                if (!allPackageReferences.Contains(name))
                {
                    context.Logger.LogInformation("Removing unused PackageVersion: {Package} from {File}", name, path);
                    pv.Remove();
                    removed++;
                    modified = true;
                }
            }

            // Remove PackageReference entries that duplicate a GlobalPackageReference
            foreach (var pr in doc.Descendants("PackageReference").ToList())
            {
                var name = pr.Attribute("Include")?.Value;
                if (name is null) continue;
                if (globalPackageReferences.Contains(name))
                {
                    context.Logger.LogInformation("Removing duplicate PackageReference (covered by GlobalPackageReference): {Package} from {File}", name, path);
                    pr.Remove();
                    removed++;
                    modified = true;
                }
            }

            if (modified)
                doc.Save(path, SaveOptions.None);
        }

        context.Logger.LogInformation("Removed {Count} unused/duplicate entries across {Files} files", removed, projectFiles.Count);
        return new(removed, projectFiles.Count);
    }

    public record Result(int EntriesRemoved, int FilesScanned);
}

file record GitLsFilesOptions : CommandLineToolOptions;
