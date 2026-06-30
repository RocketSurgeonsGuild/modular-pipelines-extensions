using GitignoreParserNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using ModularPipelines.FileSystem;
using ModularPipelines.Git;
using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class SharedSettings(IServiceProvider serviceProvider, IConfiguration configuration)
{
    public Folder RootDirectory => field ??= GetConfigurationFolder(nameof(RootDirectory)) ?? serviceProvider.GetRequiredService<IGit>().RootDirectory;

    public Folder TempDirectory => ( field ??= GetConfigurationFolder(nameof(TempDirectory)) ?? RootDirectory / ".temp" ).EnsureExists();

    public Uri? PackageIconUrl => field ??= configuration.GetValue<Uri?>(nameof(PackageIconUrl), null);
    public File PackageIconFile => field ??= TempDirectory.GetFile("packageicon.png");

    public Folder? GetConfigurationFolder(string key)
    {
        return configuration.GetValue<string?>(key) is string path
            ? Path.IsPathFullyQualified(path) ? new(path) : new Folder(Path.Combine(RootDirectory.Path, path))
            : null;
    }

    public File? GetConfigurationFile(string key)
    {
        return configuration.GetValue<string?>(key) is string path
            ? Path.IsPathFullyQualified(path) ? new(path) : new File(Path.Combine(RootDirectory.Path, path))
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
