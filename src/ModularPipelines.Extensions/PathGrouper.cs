using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace build.library;

/// <summary>
/// Groups command line file arguments to avoid Windows command-length limits.
/// </summary>
public static class PathGrouper
{
    /// <summary>
    /// Groups relative paths into command-safe chunks.
    /// </summary>
    /// <param name="paths">The relative paths to group.</param>
    /// <returns>Path groups suitable for one command invocation each.</returns>
    public static IEnumerable<ImmutableList<string>> GroupPaths(ImmutableList<string> paths)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return paths;
            yield break;
        }

        var currentGroup = ImmutableList.CreateBuilder<string>();
        var currentLength = 0;

        foreach (var path in paths)
        {
            var pathLength = path.Length;
            if (currentLength + pathLength + 1 > MaxCommandLineLength && currentGroup.Count > 0)
            {
                yield return currentGroup.ToImmutable();
                currentGroup.Clear();
                currentLength = 0;
            }

            currentGroup.Add(path);
            currentLength += pathLength + 1;
        }

        if (currentGroup.Count > 0)
        {
            yield return currentGroup.ToImmutable();
        }
    }

    private const int MaxCommandLineLength = 7500;
}
