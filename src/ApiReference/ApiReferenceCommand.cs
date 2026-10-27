using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace Rocket.Surgery.ApiReference;

internal sealed record ApiReferenceOptions(FileInfo Solution, string Configuration, string Framework, DirectoryInfo Output, DirectoryInfo SourceRoot);

internal static class ApiReferenceCommand
{
    public static async Task<int> RunAsync(ApiReferenceOptions options, CancellationToken cancellationToken)
    {
        if (!options.Solution.Exists)
        {
            await Console.Error.WriteLineAsync($"Solution not found: {options.Solution.FullName}").ConfigureAwait(false);
            return 1;
        }

        var projects = await SolutionProjects.ReadAsync(options.Solution, cancellationToken).ConfigureAwait(false);
        var packages = new ConcurrentBag<PackageInput>();
        var failures = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(
                projects,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
                async (project, token) =>
                {
                    try
                    {
                        if (await PackageInput.ResolveAsync(project, options, token).ConfigureAwait(false) is { } package)
                        {
                            packages.Add(package);
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        failures.Add(ex.Message);
                    }
                }
            )
           .ConfigureAwait(false);

        foreach (var failure in failures)
        {
            await Console.Error.WriteLineAsync(failure).ConfigureAwait(false);
        }

        if (!failures.IsEmpty)
        {
            return 1;
        }

        if (packages.IsEmpty)
        {
            await Console.Error.WriteLineAsync($"No packable projects with an assembly were found in {options.Solution.Name}.").ConfigureAwait(false);
            return 1;
        }

        options.Output.Create();
        // Shared across packages: they mostly reference the same framework and package assemblies.
        var referenceCache = new ConcurrentDictionary<string, PortableExecutableReference>(StringComparer.OrdinalIgnoreCase);
        var written = new ConcurrentBag<string>();
        var failed = 0;
        Parallel.ForEach(
            packages,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            package =>
            {
                var outputFile = Path.Combine(options.Output.FullName, $"{package.PackageId}.json");
                try
                {
                    if (PackageJsonGenerator.PackageJsonGenerator.GeneratePackageJson(
                            package.AssemblyPath,
                            package.References,
                            outputFile,
                            package.Version,
                            package.PackageId,
                            targetFrameworkOverride: package.TargetFramework,
                            referenceCache: referenceCache,
                            sourceRoot: options.SourceRoot.FullName
                        ))
                    {
                        written.Add(outputFile);
                    }
                }
#pragma warning disable CA1031 // One package's failure is reported, and fails the run, after the others are generated.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    Console.Error.WriteLine($"FAILED [{package.PackageId}]: {ex.Message}");
                    Interlocked.Increment(ref failed);
                }
            }
        );

        // Packages that were removed or lost their public API.
        foreach (var stale in Directory.EnumerateFiles(options.Output.FullName, "*.json").Except(written, StringComparer.OrdinalIgnoreCase))
        {
            File.Delete(stale);
            Console.WriteLine($"Removed: {stale}");
        }

        Console.WriteLine($"{written.Count} package(s) documented, {failed} failed.");
        return failed > 0 ? 1 : 0;
    }
}
