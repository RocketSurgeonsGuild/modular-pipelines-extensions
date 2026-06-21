using ModularPipelines.Options;

namespace build.library.Conventions;

internal class RootDirectoryInterceptor(
    SharedSettings sharedSettings) :
ICommandInterceptor<CommandLineToolOptions>
{
    public ValueTask<(CommandLineToolOptions Options, CommandExecutionOptions? ExecutionOptions)> InterceptAsync(CommandLineToolOptions options, CommandExecutionOptions? executionOptions, CancellationToken cancellationToken)
    {
        executionOptions ??= new CommandExecutionOptions();
        return ValueTask.FromResult((options, executionOptions with
        {
            WorkingDirectory = executionOptions?.WorkingDirectory ?? sharedSettings.RootDirectory
        }));
    }

}
