using System.Collections.Immutable;
using System.Xml.Linq;
using GitignoreParserNet;
using Microsoft.Extensions.Logging;
using ModularPipelines.Options;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

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
        var matcher = await sharedSettings.GetMatcher(m => m
        .AddInclude("**/*.csproj")
        .AddInclude("**/*.props")
        .AddInclude("**/*.targets")
        .AddInclude("**/*.cs"), cancellationToken);
        var gitIgnoreFile = sharedSettings.RootDirectory.GetFile(".gitignore");
        var parser = gitIgnoreFile.Exists
            ? new GitignoreParser(await gitIgnoreFile.ReadAsync(cancellationToken))
            : new GitignoreParser("");

        var listedFiles = sharedSettings.RootDirectory
        .GetFiles(f => matcher(f.Path))
        .ToImmutableList();

        var codePackageReferences = listedFiles.Where(f => f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(z => File.ReadAllLines(z.Path)
            .Where(z => z.StartsWith("#:package "))
            .Select(l => l["#:package ".Length..].Trim())
        )
                                  .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        context.Logger.LogInformation("Found {Count} package references in code files", codePackageReferences.Count);

        var projectFiles = listedFiles.Where(f => !f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList();
        // Load all documents and collect all PackageReference names across the whole repo
        var documents = projectFiles
                       .Select(f => (Path: f, Doc: XDocument.Load(f.Path, LoadOptions.PreserveWhitespace)))
                       .ToList();

        var allPackageReferences = documents
                                  .SelectMany(d => d.Doc.Descendants("PackageReference").Concat(d.Doc.Descendants("GlobalPackageReference")))
                                  .Select(e => e.Attribute("Include")?.Value)
                                  .Where(v => v is not null)
                                  .Concat(codePackageReferences)
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
