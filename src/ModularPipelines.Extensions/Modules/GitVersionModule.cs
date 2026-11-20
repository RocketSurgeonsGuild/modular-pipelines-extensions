using System.Reflection;
using Microsoft.Extensions.Logging;
using ModularPipelines.Git;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>Reads the version from GitVersion, exports each value as a <c>GITVERSION_*</c> environment variable and logs the full version.</summary>
/// <param name="gitVersioning">Runs GitVersion.</param>
public partial class GitVersionModule(IGitVersioning gitVersioning) : Module<GitVersionModule.Result>
{
    /// <inheritdoc />
    protected override async Task<Result> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var info = await gitVersioning.GetVersioningInformationAsync(cancellationToken);

        SetEnvironmentVariables(info);

        LogGitversionFullsemver(context.Logger, info.FullSemVer);
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

    /// <summary>The version GitVersion computed.</summary>
    /// <param name="Info">Everything GitVersion returned.</param>
    public record Result(GitVersionInformation Info)
    {
        /// <summary>The <c>major.minor.patch</c> version.</summary>
        public string? MajorMinorPatch => Info.MajorMinorPatch;
        /// <summary>The full semantic version, with build metadata.</summary>
        public string? FullSemVer => Info.FullSemVer;
        /// <summary>The semantic version.</summary>
        public string? SemVer => Info.SemVer;
        /// <summary>The pre-release tag with its leading dash, or empty for a release.</summary>
        public string? PreReleaseTagWithDash => Info.PreReleaseTagWithDash;
        /// <summary>Whether this is a release version: one without a pre-release tag.</summary>
        public bool IsRelease => string.IsNullOrEmpty(Info.PreReleaseTagWithDash);

        /// <summary>Every GitVersion value by name, passed to the build as MSBuild properties.</summary>
        public IReadOnlyList<KeyValue> Properties { get; } = typeof(GitVersionInformation)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => new KeyValue(p.Name, p.GetValue(Info)?.ToString() ?? ""))
            .ToArray();
    }

    [LoggerMessage(LogLevel.Information, "GitVersion: {FullSemVer}")]
    static partial void LogGitversionFullsemver(ILogger logger, string? fullSemVer);
}
