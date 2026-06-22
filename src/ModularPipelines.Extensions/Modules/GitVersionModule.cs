using System.Reflection;
using Microsoft.Extensions.Logging;
using ModularPipelines.Git;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Git.Models;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

public partial class GitVersionModule(IGitVersioning gitVersioning) : Module<GitVersionModule.Result>
{
    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var info = await gitVersioning.GetGitVersioningInformation();

        SetEnvironmentVariables(info);

        context.Logger.LogInformation("GitVersion: {FullSemVer}", info.FullSemVer);
        return new Result(info);
    }

    private static void SetEnvironmentVariables(GitVersionInformation info)
    {
        foreach (var prop in typeof(GitVersionInformation).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = prop.GetValue(info)?.ToString();
            if (value is not null)
                Environment.SetEnvironmentVariable($"GITVERSION_{prop.Name.ToUpperInvariant()}", value);
        }
    }

    public record Result(GitVersionInformation Info)
    {
        public string? MajorMinorPatch => Info.MajorMinorPatch;
        public string? FullSemVer => Info.FullSemVer;
        public string? SemVer => Info.SemVer;
        public string? PreReleaseTagWithDash => Info.PreReleaseTagWithDash;
        public bool IsRelease => string.IsNullOrEmpty(Info.PreReleaseTagWithDash);

        public IEnumerable<KeyValue> Properties { get; } = typeof(GitVersionInformation)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => new KeyValue(p.Name, p.GetValue(Info)?.ToString() ?? ""))
            .ToArray();
    }
}
