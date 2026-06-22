using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.Options;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

namespace Rocket.Surgery.ModularPipelines.Extensions.Interception;

public static class CommandInterceptorExtensions
{
    public static IServiceCollection AddCommandInterceptor<T>(this IServiceCollection services) where T : class, ICommandInterceptor
    {
        return services.AddScoped<ICommandInterceptor, T>();
    }
    public static IServiceCollection AddCommandInterceptors<T>(this IServiceCollection services) where T : class
    {
        var items = typeof(T).GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandInterceptor<>))
            .ToArray();
        foreach (var item in items)
        {
            Func<IServiceProvider, T> factory = provider => provider.GetRequiredService<T>();
            services.AddScoped<T>();
            services.AddScoped(item, factory);
            services.AddScoped(typeof(ICommandInterceptor), typeof(TypedInterceptor<>).MakeGenericType(item.GetGenericArguments()[0]));
        }
        return services;
    }

    public static IServiceCollection AddCommandInterceptor(this IServiceCollection services, Func<CommandLineToolOptions, CommandExecutionOptions?, CancellationToken, ValueTask<(CommandLineToolOptions Options, CommandExecutionOptions? ExecutionOptions)>> interceptFunc)
    {
        return services.AddScoped<ICommandInterceptor>(_ => new InlineInterceptor(interceptFunc));
    }

    public static IServiceCollection AddCommandInterceptor<TOptions>(this IServiceCollection services, Func<TOptions, CommandExecutionOptions?, CancellationToken, ValueTask<(CommandLineToolOptions Options, CommandExecutionOptions? ExecutionOptions)>> interceptFunc)
    where TOptions : CommandLineToolOptions
    {
        return services.AddScoped<ICommandInterceptor>(_ => new InlineOptionsCommandInterceptor<TOptions>(interceptFunc));
    }

    private class InlineOptionsCommandInterceptor<TOptions>(Func<TOptions, CommandExecutionOptions?, CancellationToken, ValueTask<(CommandLineToolOptions Options, CommandExecutionOptions? ExecutionOptions)>> interceptFunc) : ICommandInterceptor where TOptions : CommandLineToolOptions
    {
        ValueTask<(CommandLineToolOptions Options, CommandExecutionOptions? ExecutionOptions)> ICommandInterceptor.InterceptAsync(CommandLineToolOptions options, CommandExecutionOptions? executionOptions, CancellationToken cancellationToken)
        {
            return options is TOptions typedOptions
                ? interceptFunc(typedOptions, executionOptions, cancellationToken)
                : ValueTask.FromResult((options, executionOptions));
        }
    }

    private class InlineInterceptor(Func<CommandLineToolOptions, CommandExecutionOptions?, CancellationToken, ValueTask<(CommandLineToolOptions Options, CommandExecutionOptions? ExecutionOptions)>> interceptFunc) : ICommandInterceptor
    {
        public ValueTask<(CommandLineToolOptions Options, CommandExecutionOptions? ExecutionOptions)> InterceptAsync(CommandLineToolOptions options, CommandExecutionOptions? executionOptions = null, CancellationToken cancellationToken = default) => interceptFunc(options, executionOptions, cancellationToken);
    }

    private class TypedInterceptor<TOptions>(ICommandInterceptor<TOptions> interceptor) : ICommandInterceptor where TOptions : CommandLineToolOptions
    {
        public async ValueTask<(CommandLineToolOptions Options, CommandExecutionOptions? ExecutionOptions)> InterceptAsync(CommandLineToolOptions options, CommandExecutionOptions? executionOptions = null, CancellationToken cancellationToken = default) =>
        options is TOptions typedOptions
            ? await interceptor.InterceptAsync(typedOptions, executionOptions, cancellationToken)
            : (options, executionOptions);
    }
}
