using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Rocket.Surgery.ApiReference;

internal static class SolutionProjects
{
    /// <summary>The full paths of the C# projects in <paramref name="solution" />.</summary>
    public static async Task<IReadOnlyList<string>> ReadAsync(FileInfo solution, CancellationToken cancellationToken)
    {
        var serializer = SolutionSerializers.GetSerializerByMoniker(solution.FullName)
         ?? throw new InvalidOperationException($"Not a solution file: {solution.FullName}");
        var model = await serializer.OpenAsync(solution.FullName, cancellationToken).ConfigureAwait(false);
        return model
              .SolutionProjects
              .Select(project => Path.GetFullPath(Path.Combine(solution.DirectoryName!, project.FilePath.Replace('\\', Path.DirectorySeparatorChar))))
              .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
              .ToArray();
    }
}
