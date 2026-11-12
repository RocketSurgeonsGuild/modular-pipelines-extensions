using Joinery.Dovetail;
using Joinery.Dovetail.Attributes;
using Joinery.Dovetail.Configuration.Yaml;
using Joinery.Dovetail.Infrastructure;
using Joinery.Dovetail.Joints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace Rocket.Surgery.ModularPipelines.Extensions.Joints;

/// <summary>
///     Reads the build host's settings files (<c>appsettings.yaml</c>, <c>appsettings.local.json</c> and the rest) from the folder of
///     the build scripts, so they apply as they are on disk, without a rebuild, and override the copies the build put in the output folder.
/// </summary>
/// <remarks>
///     <para>
///         The folder is the <c>BuildScriptsRoot</c> string in the context when the build host sets one
///         (<c>ConfigureDovetail(z =&gt; z.Set("BuildScriptsRoot", path))</c>), otherwise the folder of a file-based build host's
///         <c>.cs</c> file. Without either it adds nothing.
///     </para>
///     <para>
///         Each format is read in the order <c>appsettings</c>, <c>appsettings.{Environment}</c>, <c>appsettings.local</c>, JSON before
///         YAML, all optional. The joint runs after the other configuration joints, so these files win over theirs; the environment
///         variables and the command line still win over all of them.
///     </para>
/// </remarks>
[DovetailExport]
public sealed class BuildScriptsRootJoint : IConfigurationJoint
{
    /// <summary>The context key of the folder the settings files are read from.</summary>
    public const string BuildScriptsRootKey = "BuildScriptsRoot";

    int IDovetailJoint.Priority => int.MaxValue;

    /// <inheritdoc />
    public void Register(IDovetailContext context, IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(builder);
        if (GetBuildScriptsRoot(context) is not { } root) return;

        var fileProvider = new PhysicalFileProvider(root);
        // What the pipeline itself reads from files, it reads from there as well.
        context.Get<PipelineBuilder>()?.Configuration.SetFileProvider(fileProvider);

        var environmentName = context.Get<string>("EnvironmentName");
        foreach (var name in (string?[])["appsettings", environmentName is { Length: > 0 } ? $"appsettings.{environmentName}" : null, "appsettings.local"])
        {
            if (name is null) continue;
            builder.Add(new JsonConfigurationSource { FileProvider = fileProvider, Path = $"{name}.json", Optional = true, ReloadOnChange = true });
            builder.Add(new YamlConfigurationSource { FileProvider = fileProvider, Path = $"{name}.yaml", Optional = true, ReloadOnChange = true });
            builder.Add(new YamlConfigurationSource { FileProvider = fileProvider, Path = $"{name}.yml", Optional = true, ReloadOnChange = true });
        }
    }

    /// <summary>The folder the settings files are read from, if there is one.</summary>
    /// <param name="context">The context.</param>
    internal static string? GetBuildScriptsRoot(IDovetailContext context) =>
        context.Get<string>(BuildScriptsRootKey) is { Length: > 0 } root ? root
        // Set by `dotnet run` for a file-based app.
        : AppContext.GetData("EntryPointFileDirectoryPath") is string { Length: > 0 } directory ? directory
        : null;
}
