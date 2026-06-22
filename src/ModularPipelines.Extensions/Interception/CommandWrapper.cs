using ModularPipelines.Context.Domains.Shell;
using ModularPipelines.Options;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

public sealed class CommandWrapper(ICommand command, IEnumerable<ICommandInterceptor> interceptors) : ICommand, ICommandContext
{
    public async Task<CommandResult> ExecuteCommandLineTool(CommandLineToolOptions options, CommandExecutionOptions? executionOptions = null, CancellationToken cancellationToken = default)
    {
        foreach (var interceptor in interceptors)
        {
            try
            {
                (options, executionOptions) = await interceptor.InterceptAsync(options, executionOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                // ?
            }
        }

        return await command.ExecuteCommandLineTool(options, executionOptions ?? new(), cancellationToken);
    }
}
