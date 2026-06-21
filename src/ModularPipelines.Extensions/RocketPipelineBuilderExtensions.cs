using build.library.Conventions;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Rocket.Surgery.Conventions.Configuration;

namespace build.library;

[PublicAPI]
//[EditorBrowsable(EditorBrowsableState.Never)]
public static class RocketPipelineBuilderExtensions
{
    //    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<PipelineBuilder> Configure(
        this PipelineBuilder pipelineBuilder,
        ConventionContextBuilder contextBuilder,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(pipelineBuilder);

        if (contextBuilder.Get<string>("BuildScriptsRoot") is { } buildScriptsRoot)
            pipelineBuilder.Configuration.SetFileProvider(new PhysicalFileProvider(buildScriptsRoot));

        contextBuilder
           .AddIfMissing(HostType.Live)
           .AddIfMissing(pipelineBuilder)
           .AddIfMissing(pipelineBuilder.GetType(), pipelineBuilder)
           .AddIfMissing(pipelineBuilder.Configuration)
           .AddIfMissing<IConfiguration>(pipelineBuilder.Configuration)
           .AddIfMissing(pipelineBuilder.Configuration.GetType(), pipelineBuilder.Configuration)
           .AddIfMissing(pipelineBuilder.Environment)
           .AddIfMissing(nameof(pipelineBuilder.Environment.ApplicationName), pipelineBuilder.Environment.ApplicationName)
           .AddIfMissing(nameof(pipelineBuilder.Environment.ContentRootPath), pipelineBuilder.Environment.ContentRootPath)
           .AddIfMissing(nameof(pipelineBuilder.Environment.EnvironmentName), pipelineBuilder.Environment.EnvironmentName)
           .AddIfMissing(pipelineBuilder.Environment.GetType(), pipelineBuilder.Environment);

        var conventionContext = await ConventionContext.FromAsync(contextBuilder, cancellationToken).ConfigureAwait(false);

        await SharedHostConfigurationAsync(conventionContext, pipelineBuilder, cancellationToken).ConfigureAwait(false);
        await pipelineBuilder.Services.ApplyConventionsAsync(conventionContext, cancellationToken).ConfigureAwait(false);

        await ApplyConventions(conventionContext, pipelineBuilder, cancellationToken).ConfigureAwait(false);
        return pipelineBuilder;
    }


    internal static async ValueTask SharedHostConfigurationAsync(
        IConventionContext context,
        PipelineBuilder pipelineBuilder,
        CancellationToken cancellationToken
    )
    {
        // This code is duplicated per host (web host, generic host, and wasm host)
        void insertNamedSource(string name)
        {
            pipelineBuilder.Configuration.InsertConfigurationSourceAfter(
                sources => sources
                          .OfType<FileConfigurationSource>()
                          .FirstOrDefault(x => string.Equals(
                                              x.Path,
                                              $"{name}.{pipelineBuilder.Environment.EnvironmentName}.json",
                                              StringComparison.OrdinalIgnoreCase
                                          )
                           ),
                new IConfigurationSource[]
                {
                    new JsonConfigurationSource
                    {
                        FileProvider = pipelineBuilder.Configuration.GetFileProvider(),
                        Path = $"{name}.local.json",
                        Optional = true,
                        ReloadOnChange = true,
                    },
                }
            );

            pipelineBuilder.Configuration.ReplaceConfigurationSourceAt(
                sources => sources
                          .OfType<FileConfigurationSource>()
                          .FirstOrDefault(x => string.Equals(x.Path, $"{name}.json", StringComparison.OrdinalIgnoreCase)
                           ),
                context
                   .GetOrAdd<List<ConfigurationBuilderApplicationDelegate>>(() => [])
                   .SelectMany(z => z.Invoke(pipelineBuilder.Configuration))
                   .Select(z => z.Factory(null))
            );

            pipelineBuilder.Configuration.ReplaceConfigurationSourceAt(
                sources => sources
                          .OfType<FileConfigurationSource>()
                          .FirstOrDefault(x => string.Equals(
                                              x.Path,
                                              $"{name}.{pipelineBuilder.Environment.EnvironmentName}.json",
                                              StringComparison.OrdinalIgnoreCase
                                          )
                           ),
                context
                   .GetOrAdd<List<ConfigurationBuilderEnvironmentDelegate>>(() => [])
                   .SelectMany(z => z.Invoke(pipelineBuilder.Configuration, pipelineBuilder.Environment.EnvironmentName))
                   .Select(z => z.Factory(null))
            );

            pipelineBuilder.Configuration.ReplaceConfigurationSourceAt(
                sources => sources
                          .OfType<FileConfigurationSource>()
                          .FirstOrDefault(x => string.Equals(x.Path, $"{name}.local.json", StringComparison.OrdinalIgnoreCase)),
                context
                   .GetOrAdd<List<ConfigurationBuilderEnvironmentDelegate>>(() => [])
                   .SelectMany(z => z.Invoke(pipelineBuilder.Configuration, "local"))
                   .Select(z => z.Factory(null))
            );
        }

        insertNamedSource("appsettings");
        insertNamedSource(pipelineBuilder.Environment.ApplicationName);

        IConfigurationSource? source = null;
        foreach (var item in pipelineBuilder.Configuration.Sources.Reverse())
        {
            if (item is CommandLineConfigurationSource
             || ( item is EnvironmentVariablesConfigurationSource env
                 && ( string.IsNullOrWhiteSpace(env.Prefix) || string.Equals(env.Prefix, "RSG_", StringComparison.OrdinalIgnoreCase) ) )
             || ( item is FileConfigurationSource a && string.Equals(a.Path, "secrets.json", StringComparison.OrdinalIgnoreCase) ))
            {
                continue;
            }

            source = item;
            break;
        }

        var index = source is null
            ? pipelineBuilder.Configuration.Sources.Count - 1
            : pipelineBuilder.Configuration.Sources.IndexOf(source);
        // Insert after all the normal configuration but before the environment specific configuration

        var cb = await new ConfigurationBuilder().ApplyConventionsAsync(context, pipelineBuilder.Configuration, cancellationToken).ConfigureAwait(false);
        if (cb.Sources is { Count: > 0 })
        {
            pipelineBuilder.Configuration.Sources.Insert(
                index + 1,
                new ChainedConfigurationSource
                {
                    Configuration = cb.Build(),
                    ShouldDisposeConfiguration = true,
                }
            );
        }

        pipelineBuilder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["RocketSurgeryConventions:HostType"] = context.GetHostType().ToString() }
        );
    }


    internal static void InsertConfigurationSourceAfter<T>(
        this IConfigurationBuilder builder,
        Func<IList<IConfigurationSource>, T?> getSource,
        IEnumerable<IConfigurationSource> createSourceFrom
    )
        where T : IConfigurationSource
    {
        var source = getSource(builder.Sources);
        if (source != null)
        {
            var index = builder.Sources.IndexOf(source);
            // We add in reverse order to keep the same order going in.
            foreach (var newSource in createSourceFrom.Reverse())
            {
                builder.Sources.Insert(index + 1, newSource);
            }
        }
        else
        {
            foreach (var newSource in createSourceFrom)
            {
                builder.Sources.Add(newSource);
            }
        }
    }

    internal static void ReplaceConfigurationSourceAt<T>(
        this IConfigurationBuilder builder,
        Func<IList<IConfigurationSource>, T?> getSource,
        IEnumerable<IConfigurationSource> createSourceFrom
    ) where T : class, IConfigurationSource
    {
        var source = getSource(builder.Sources);
        if (source != null)
        {
            var index = builder.Sources.IndexOf(source);
            builder.Sources.RemoveAt(index);
            // We add in reverse order to keep the same order going in.
            foreach (var newSource in createSourceFrom.Reverse())
            {
                builder.Sources.Insert(index, newSource);
            }
        }
        else
        {
            foreach (var newSource in createSourceFrom)
            {
                builder.Sources.Add(newSource);
            }
        }
    }

    private static async Task<IConventionContext> ApplyConventions(
        IConventionContext context,
        PipelineBuilder builder,
        CancellationToken cancellationToken
    )
    {
        await context
             .RegisterConventions(e => e
                                      .AddHandler<IPipelineBuilderConvention>(convention => convention.Register(context, builder))
                                      .AddHandler<IPipelineBuilderAsyncConvention>(convention => convention.Register(context, builder, cancellationToken))
                                      .AddHandler<PipelineBuilderConvention>(convention => convention(context, builder))
                                      .AddHandler<PipelineBuilderAsyncConvention>(convention => convention(context, builder, cancellationToken))
              )
             .ConfigureAwait(false);
        return context;
    }
}
