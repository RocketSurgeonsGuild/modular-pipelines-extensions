using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.Git;

namespace Rocket.Surgery.ModularPipelines.Extensions;

internal static class IntegrationRegistration
{
    // ModularPipelines registers an integration only if its assembly is loaded when the pipeline is built, and it loads the referenced
    // ones itself only where dynamic code is supported, which a file-based app (PublishAot by default) turns off even under `dotnet run`.
    // This assembly is loaded by then, so it registers the integrations its modules use. Both registrations only TryAdd.
    [ModularPipelinesIntegration]
    public static void Register(IServiceCollection services)
    {
        services.RegisterGitContext();
        services.RegisterDotNetContext();
    }
}
