namespace Rocket.Surgery.ModularPipelines.Extensions.Conventions;

/// <summary>
///     Runs every command that does not choose its own working directory from the repository root, instead of the pipeline's working
///     directory.
/// </summary>
/// <remarks>
///     By the time an interceptor runs, a command without a working directory has already been given the pipeline's, so that is what
///     "not chosen" looks like here.
/// </remarks>
internal sealed class RootDirectoryInterceptor(SharedSettings sharedSettings, string pipelineWorkingDirectory) : ICommandInterceptor
{
    private readonly string _pipelineWorkingDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pipelineWorkingDirectory));

    public ValueTask<CommandResult> InvokeAsync(CommandInvocation invocation, CommandDelegate next, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.TrimEndingDirectorySeparator(invocation.WorkingDirectory), _pipelineWorkingDirectory, StringComparison.Ordinal))
            return next(invocation, cancellationToken);

        var rootDirectory = sharedSettings.RootDirectory.Path;
        return string.Equals(Path.TrimEndingDirectorySeparator(rootDirectory), _pipelineWorkingDirectory, StringComparison.Ordinal)
            ? next(invocation, cancellationToken)
            : next(invocation with { WorkingDirectory = rootDirectory }, cancellationToken);
    }
}
