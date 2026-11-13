using System.Collections.Immutable;
using System.Xml.Linq;
using GitignoreParserNet;
using Microsoft.Extensions.Logging;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

public partial class RemoveUnusedDependenciesModule(SharedSettings sharedSettings) : Module<RemoveUnusedDependenciesModule.Result>
{
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => SkipDecision.When(IsOnCI(),
            "Only runs on local builds")
    );

    private static bool IsOnCI() =>
        Environment.GetEnvironmentVariable("CI") is not null ||
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null;

    protected override async Task<Result> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
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
        ).ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        LogFoundCodePackageReferences(context.Logger, codePackageReferences.Count);

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
                    LogRemovingUnusedPackageVersion(context.Logger, name, path);
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
                    LogRemovingDuplicatePackageReference(context.Logger, name, path);
                    pr.Remove();
                    removed++;
                    modified = true;
                }
            }

            if (modified)
                doc.Save(path, SaveOptions.None);
        }

        LogRemovedUnusedDuplicateEntries(context.Logger, removed, projectFiles.Count);
        return new(removed, projectFiles.Count);
    }

    public record Result(int EntriesRemoved, int FilesScanned);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Removing unused PackageVersion: {Package} from {File}")]
    private static partial void LogRemovingUnusedPackageVersion(ILogger logger, string package, string file);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Removing duplicate PackageReference (covered by GlobalPackageReference): {Package} from {File}")]
    private static partial void LogRemovingDuplicatePackageReference(ILogger logger, string package, string file);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Found {Count} package references in code files")]
    private static partial void LogFoundCodePackageReferences(ILogger logger, int count);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Removed {Count} unused/duplicate entries across {Files} files")]
    private static partial void LogRemovedUnusedDuplicateEntries(ILogger logger, int count, int files);
}
