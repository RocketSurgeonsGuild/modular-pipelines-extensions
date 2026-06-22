using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.Conventions.DependencyInjection;
using Rocket.Surgery.ModularPipelines.Extensions.Interception;

namespace Rocket.Surgery.ModularPipelines.Extensions.Conventions;

[ExportConvention]
internal class ModulePipelinesConvention : IServiceAsyncConvention
{
    public ValueTask Register(IConventionContext context, IConfiguration configuration, IServiceCollection services, CancellationToken cancellationToken)
    {
        services.AddCommandInterceptors<SolutionInterceptor>();
        // services.AddCommandInterceptors<LoggingInterceptor>();
        services.AddCommandInterceptors<RootDirectoryInterceptor>();
        return ValueTask.CompletedTask;
    }
}
