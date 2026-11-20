using GitignoreParserNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Rocket.Surgery.ModularPipelines.Extensions;

/// <summary>The settings every module shares: the root and temp folders, the package icon, and how to read paths from configuration.</summary>
/// <param name="configuration">The pipeline configuration.</param>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class SharedSettings(IConfiguration configuration)
{
    /// <summary>The repository's root folder. Read from <c>RootDirectory</c>; the git root above the current directory by default.</summary>
    // Found on disk rather than through IGitInformation: that runs git, and every command passes through RootDirectoryInterceptor, which
    // reads this property.
    public FolderPath RootDirectory => field ??= GetConfigurationFolder(nameof(RootDirectory))
     ?? new FolderPath(PipelineDirectory.FindGitRoot(Path.Combine(Directory.GetCurrentDirectory(), "_")));

    /// <summary>The temp folder, created if missing. Read from <c>TempDirectory</c>; <c>.temp</c> under the root directory by default.</summary>
    public FolderPath TempDirectory => ( field ??= GetConfigurationFolder(nameof(TempDirectory)) ?? RootDirectory / ".temp" ).EnsureExists();

    /// <summary>Where to download the package icon from. Read from <c>PackageIconUrl</c>; none by default.</summary>
    public Uri? PackageIconUrl => field ??= configuration.GetValue<Uri?>(nameof(PackageIconUrl), null);
    /// <summary>Where the package icon is saved: <c>packageicon.png</c> in <see cref="TempDirectory" />.</summary>
    public FilePath PackageIconFile => field ??= TempDirectory.GetFile("packageicon.png");

    /// <summary>Reads a folder path from configuration, relative to <see cref="RootDirectory" /> unless it is fully qualified.</summary>
    /// <param name="key">The configuration key.</param>
    /// <returns>The folder, or <see langword="null" /> when the key is not set.</returns>
    public FolderPath? GetConfigurationFolder(string key)
    {
        return configuration.GetValue<string?>(key) is string path
            ? Path.IsPathFullyQualified(path) ? new(path) : new FolderPath(Path.Combine(RootDirectory.Path, path))
            : null;
    }

    /// <summary>Reads a file path from configuration, relative to <see cref="RootDirectory" /> unless it is fully qualified.</summary>
    /// <param name="key">The configuration key.</param>
    /// <returns>The file, or <see langword="null" /> when the key is not set.</returns>
    public FilePath? GetConfigurationFile(string key)
    {
        return configuration.GetValue<string?>(key) is string path
            ? Path.IsPathFullyQualified(path) ? new(path) : new FilePath(Path.Combine(RootDirectory.Path, path))
            : null;
    }

    /// <summary>
    ///     Builds a filter for paths under <see cref="RootDirectory" /> that the root <c>.gitignore</c> accepts and the matcher includes. The matcher
    ///     already leaves out build output, tool and IDE folders.
    /// </summary>
    /// <param name="configure">Adds the includes and excludes.</param>
    /// <param name="cancellationToken">Cancels reading <c>.gitignore</c>.</param>
    /// <returns>A function that says whether a relative path matches.</returns>
    public async ValueTask<Func<string, bool>> GetMatcher(Func<Matcher, Matcher> configure, CancellationToken cancellationToken = default)
    {
        var gitIgnoreFile = RootDirectory.GetFile(".gitignore");
        var parser = gitIgnoreFile.Exists
            ? new GitignoreParser(await gitIgnoreFile.ReadAsync(cancellationToken))
            : new GitignoreParser("");
        var matcher = new Matcher()
        .AddExclude("**/bin/**")
            .AddExclude("**/obj/**")
            .AddExclude("**/.git/**")
            .AddExclude("**/.github/**")
            .AddExclude("**/.claude/**")
            .AddExclude("**/.apm/**")
            .AddExclude("**/.agents/**")
            .AddExclude("**/.temp/**")
            .AddExclude("**/.config/**")
            .AddExclude("**/node_modules/**")
            .AddExclude("**/apm_modules/**")
            .AddExclude("**/.vs/**")
            .AddExclude("**/.idea/**")
            .AddExclude("**/.vscode/**");

        matcher = configure(matcher);

        return path => parser.Accepts(path) && matcher.Match(path).HasMatches;
    }
}
