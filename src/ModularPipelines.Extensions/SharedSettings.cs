using GitignoreParserNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using ModularPipelines.FileSystem;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class SharedSettings(IConfiguration configuration)
{
    // Found on disk rather than through IGitInformation: that runs git, and every command passes through RootDirectoryInterceptor, which
    // reads this property.
    public FolderPath RootDirectory => field ??= GetConfigurationFolder(nameof(RootDirectory))
     ?? new FolderPath(PipelineDirectory.FindGitRoot(Path.Combine(Directory.GetCurrentDirectory(), "_")));

    public FolderPath TempDirectory => ( field ??= GetConfigurationFolder(nameof(TempDirectory)) ?? RootDirectory / ".temp" ).EnsureExists();

    public Uri? PackageIconUrl => field ??= configuration.GetValue<Uri?>(nameof(PackageIconUrl), null);
    public FilePath PackageIconFile => field ??= TempDirectory.GetFile("packageicon.png");

    public FolderPath? GetConfigurationFolder(string key)
    {
        return configuration.GetValue<string?>(key) is string path
            ? Path.IsPathFullyQualified(path) ? new(path) : new FolderPath(Path.Combine(RootDirectory.Path, path))
            : null;
    }

    public FilePath? GetConfigurationFile(string key)
    {
        return configuration.GetValue<string?>(key) is string path
            ? Path.IsPathFullyQualified(path) ? new(path) : new FilePath(Path.Combine(RootDirectory.Path, path))
            : null;
    }

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
