using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.Engine;

namespace Rocket.Surgery.ModularPipelines.Extensions.Interception;

internal static class CommandInterceptorInitializer
{
    [ModuleInitializer]
    public static void Initialize() => ModularPipelinesContextRegistry.RegisterContext(collection =>
                                                                                       {
                                                                                           var commandService = collection.Single(z => z.ServiceType == typeof(ICommand));
                                                                                           collection.Remove(commandService);
                                                                                           collection.AddScoped<ICommand>(provider =>
                                                                                                                          {
                                                                                                                              var command = (ICommand)ActivatorUtilities.CreateInstance(
                                                                                                                                  provider,
                                                                                                                                  commandService.ImplementationType!
                                                                                                                              );
                                                                                                                              var interceptors = provider.GetServices<ICommandInterceptor>();
                                                                                                                              return new CommandWrapper(command, interceptors);
                                                                                                                          }
                                                                                           );
                                                                                       }
    );
}
