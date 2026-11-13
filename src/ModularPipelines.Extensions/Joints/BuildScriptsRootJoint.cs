using Joinery.Dovetail;
using Joinery.Dovetail.Attributes;
using Joinery.Dovetail.Infrastructure;
using Joinery.Dovetail.Joints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace Rocket.Surgery.ModularPipelines.Extensions.Joints;

/// <summary>
///     Points Dovetail's configuration at the folder of the build scripts, so its JSON and YAML joints read the build host's settings
///     files (<c>appsettings.yaml</c>, <c>appsettings.local.json</c> and the rest) as they are on disk, without a rebuild.
/// </summary>
/// <remarks>
///     <para>
///         The folder is the <c>BuildScriptsRoot</c> string in the context when the build host sets one
///         (<c>ConfigureDovetail(z =&gt; z.Set("BuildScriptsRoot", path))</c>), otherwise the folder of a file-based build host's
///         <c>.cs</c> file. Without either it changes nothing.
///     </para>
///     <para>
///         It runs before the other configuration joints and sets the file provider they resolve their files against: Dovetail's
///         <c>appsettings</c>, <c>appsettings.{Environment}</c> and <c>appsettings.local</c> files (and the same named after the
///         assembly), each format layered on its own. The pipeline reads its own files from there as well. The environment variables
///         and the command line still win over all of them.
///     </para>
/// </remarks>
[DovetailExport]
public sealed class BuildScriptsRootJoint : IConfigurationJoint
{
    /// <summary>The context key of the folder the settings files are read from.</summary>
    public const string BuildScriptsRootKey = "BuildScriptsRoot";

    int IDovetailJoint.Priority => int.MinValue;

    /// <inheritdoc />
    public void Register(IDovetailContext context, IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(builder);
        if (GetBuildScriptsRoot(context) is not { } root) return;

        var fileProvider = new PhysicalFileProvider(root);
        builder.SetFileProvider(fileProvider);
        context.Get<PipelineBuilder>()?.Configuration.SetFileProvider(fileProvider);
    }

    /// <summary>The folder the settings files are read from, if there is one.</summary>
    /// <param name="context">The context.</param>
    internal static string? GetBuildScriptsRoot(IDovetailContext context) =>
        context.Get<string>(BuildScriptsRootKey) is { Length: > 0 } root ? root
        // Set by `dotnet run` for a file-based app.
        : AppContext.GetData("EntryPointFileDirectoryPath") is string { Length: > 0 } directory ? directory
        : null;
}
