using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Rocket.Surgery.ApiReference;

/// <summary>A packable project's built assembly, and everything it was compiled against.</summary>
internal sealed record PackageInput(string PackageId, string Version, string TargetFramework, string AssemblyPath, string[] References)
{
    /// <summary>
    /// Asks MSBuild about <paramref name="project" />: <see langword="null" /> when it doesn't ship an assembly worth documenting
    /// (not packable, a test project, a tool, an analyzer, or an SDK package without build output).
    /// </summary>
    public static async Task<PackageInput?> ResolveAsync(string project, ApiReferenceOptions options, CancellationToken cancellationToken)
    {
        var outer = await MSBuild.QueryAsync(
                        project,
                        options.Configuration,
                        [
                            "-getProperty:IsPackable", "-getProperty:IsTestProject", "-getProperty:PackAsTool", "-getProperty:IncludeBuildOutput",
                            "-getProperty:IsRoslynComponent", "-getProperty:PackageId", "-getProperty:TargetFramework", "-getProperty:TargetFrameworks",
                        ],
                        cancellationToken
                    )
                   .ConfigureAwait(false);
        if (!outer.IsTrue("IsPackable")
         || outer.IsTrue("IsTestProject")
         || outer.IsTrue("PackAsTool")
         || outer.IsTrue("IsRoslynComponent")
         || outer.Property("IncludeBuildOutput").Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var frameworks = ( outer.Property("TargetFrameworks") is { Length: > 0 } multiple ? multiple : outer.Property("TargetFramework") )
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (PickFramework(options.Framework, frameworks) is not { } framework)
        {
            return null;
        }

        // ResolveReferences without building the referenced projects: they were built with the solution, and their reference
        // assemblies are what the compiler saw.
        var inner = await MSBuild.QueryAsync(
                        project,
                        options.Configuration,
                        [
                            $"-p:TargetFramework={framework}", "-p:BuildProjectReferences=false", "-t:ResolveReferences", "-getProperty:TargetPath",
                            "-getProperty:ProjectName", "-getItem:ReferencePath",
                        ],
                        cancellationToken
                    )
                   .ConfigureAwait(false);
        var assemblyPath = inner.Property("TargetPath");
        return  !File.Exists(assemblyPath) 
            ? throw new InvalidOperationException(
                $"{inner.Property("ProjectName")} ({framework}) isn't built: {assemblyPath} doesn't exist. Build the solution ({options.Configuration}) first."
            )
            :   new(
            outer.Property("PackageId"),
            // The version the build stamped (GitVersion's, in CI) rather than the evaluated default.
            FileVersionInfo.GetVersionInfo(assemblyPath).ProductVersion?.Split('+')[0] ?? "0.0.0",
            framework,
            assemblyPath,
            [.. inner.Items("ReferencePath")]
        );
    }

    /// <summary>
    /// <paramref name="requested" /> when the project targets it, otherwise the newest platform-neutral .NET target framework,
    /// then .NET Standard.
    /// </summary>
    internal static string? PickFramework(string requested, IReadOnlyList<string> frameworks)
    {
        if (frameworks.Contains(requested, StringComparer.OrdinalIgnoreCase))
        {
            return requested;
        }

        var neutral = frameworks.Where(z => !z.Contains('-', StringComparison.Ordinal)).ToArray();
        return neutral
              .Where(z => z.StartsWith("net", StringComparison.OrdinalIgnoreCase) && z.Length > 3 && char.IsDigit(z[3]) && z.Contains('.', StringComparison.Ordinal))
              .OrderByDescending(z => System.Version.Parse(z[3..]))
              .Concat(neutral.Where(z => z.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)).OrderDescending(StringComparer.OrdinalIgnoreCase))
              .Concat(frameworks)
              .FirstOrDefault();
    }

    private sealed class MSBuild(JsonElement root)
    {
        public static async Task<MSBuild> QueryAsync(string project, string configuration, IEnumerable<string> arguments, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(project)!,
            };
            foreach (var argument in (string[])["msbuild", project, "-nologo", $"-p:Configuration={configuration}", .. arguments])
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            // Both streams at once: a full stderr pipe would otherwise block the process while stdout is read.
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"dotnet msbuild {Path.GetFileName(project)} failed ({process.ExitCode}):\n{output}\n{errors}")
                );
            }

            using var document = JsonDocument.Parse(output);
            return new(document.RootElement.Clone());
        }

        public string Property(string name) =>
            root.TryGetProperty("Properties", out var properties) && properties.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

        public bool IsTrue(string name) => Property(name).Equals("true", StringComparison.OrdinalIgnoreCase);

        public IEnumerable<string> Items(string name) =>
            root.TryGetProperty("Items", out var items) && items.TryGetProperty(name, out var values)
                ? values.EnumerateArray().Select(item => item.GetProperty("FullPath").GetString()!)
                : [];
    }
}
