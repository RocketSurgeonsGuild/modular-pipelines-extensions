using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GitignoreParserNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Removes <c>PackageVersion</c> entries no project or file-based app references, and <c>PackageReference</c> entries a
///     <c>GlobalPackageReference</c> already covers. Opt in with <c>RemoveUnusedDependencies: true</c>; it never runs on CI.
/// </summary>
/// <remarks>
///     Off by default: it rewrites the repository's project files on every local build, and a version that only pins a transitive
///     package (central transitive pinning) looks unused to it. The files keep their encoding, byte-order mark and XML declaration.
/// </remarks>
public partial class RemoveUnusedDependenciesModule(SharedSettings sharedSettings, IConfiguration configuration) : Module<RemoveUnusedDependenciesModule.Result>
{
    protected override void Configure(ModuleConfigurationBuilder module) => module.WithSkipWhen(
        _ => !configuration.GetValue("RemoveUnusedDependencies", false)
            ? SkipDecision.Skip("Not enabled; set RemoveUnusedDependencies: true to run it")
            : SkipDecision.When(IsOnCI(), "Only runs on local builds")
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
            // #:package Name@Version: only the name is what a PackageVersion lists.
            .Select(l => l["#:package ".Length..].Trim().Split('@', 2)[0].Trim())
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
                Save(doc, path);
        }

        LogRemovedUnusedDuplicateEntries(context.Logger, removed, projectFiles.Count);
        return new(removed, projectFiles.Count);
    }

    /// <summary>Writes the document back the way it was read: with or without a byte-order mark and an XML declaration.</summary>
    private static void Save(XDocument document, FilePath path)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var bytes = File.ReadAllBytes(path.Path);
        var hadByteOrderMark = bytes.AsSpan().StartsWith(preamble);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(hadByteOrderMark),
            OmitXmlDeclaration = document.Declaration is null,
            Indent = false,
        };
        using (var writer = XmlWriter.Create(path.Path, settings))
        {
            document.Save(writer);
        }

        // XmlWriter drops the final newline the file ended with.
        if (bytes.Length > 0 && bytes[^1] == (byte)'\n')
        {
            File.AppendAllText(path.Path, bytes.Length > 1 && bytes[^2] == (byte)'\r' ? "\r\n" : "\n");
        }
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
