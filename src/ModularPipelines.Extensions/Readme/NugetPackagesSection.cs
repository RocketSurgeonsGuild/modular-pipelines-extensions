using System.Security.Cryptography;
using System.Text;

namespace Rocket.Surgery.ModularPipelines.Extensions.Readme;

internal class NugetPackagesSection : IReadmeSection
{
    /// <summary>
    ///     Get the list of nuget packages with references that ensure uniqueness
    /// </summary>
    /// <param name="config"></param>
    /// <param name="references"></param>
    /// <param name="packageName"></param>
    /// <returns></returns>
    public static string GetResult(IDictionary<string, object?> config, IMarkdownReferences references, string packageName)
    {
        var hash = Convert
                  .ToBase64String(MD5.HashData(Encoding.ASCII.GetBytes(packageName)))
                  .Replace("=", "")
                   [10..]
                  .ToLowerInvariant();
        var nugetUrlReference = references.AddReference($"nuget-{hash}", NugetUrl(packageName));
        var nugetVersionBadge = references.AddReference(
            $"nuget-version-{hash}-badge",
            NuGetVersionBadge(packageName),
            "NuGet Version"
        );
        var nugetDownloadsBadge = references.AddReference(
            $"nuget-downloads-{hash}-badge",
            NuGetDownloadsBadge(packageName),
            "NuGet Downloads"
        );
        return $"| {packageName} | [!{nugetVersionBadge}!{nugetDownloadsBadge}]{nugetUrlReference} |";
    }

    public async Task<string> Process(
        IDictionary<string, object?> config,
        IMarkdownReferences references,
        IModuleContext context
    )
    {
        var sb = new StringBuilder();

        sb.AppendLine("| Package | NuGet |");
        sb.AppendLine("| ------- | ----- |");
        // var packageNames = context.Solution.WherePackable().Select(x => x.PackageId);
        // foreach (var package in packageNames.Order())
        // {
        //     sb.AppendLine(GetResult(config, references, package));
        // }

        return sb.ToString();
    }

    public string ConfigKey { get; } = "";

    public string Name { get; } = "nuget packages";

    private static string NuGetDownloadsBadge(string packageName) =>
        $"https://img.shields.io/nuget/dt/{packageName}.svg?color=004880&logo=nuget&style=flat-square";

    private static string NugetUrl(string packageName) => $"https://www.nuget.org/packages/{packageName}/";

    private static string NuGetVersionBadge(string packageName) =>
        $"https://img.shields.io/nuget/v/{packageName}.svg?color=004880&logo=nuget&style=flat-square";
}
