using ModularPipelines.Options;
using Rocket.Surgery.ModularPipelines.Extensions.Interception;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

namespace Rocket.Surgery.ModularPipelines.Extensions.Conventions;

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
