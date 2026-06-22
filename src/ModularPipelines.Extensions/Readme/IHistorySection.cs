namespace Rocket.Surgery.ModularPipelines.Extensions.Readme;

/// <summary>
///     Interface is used to add another badge to the `history badges` container in the readme.
/// </summary>
public interface IHistorySection
{
    /// <summary>
    ///     Returns the markdown that will produce the badge
    /// </summary>
    /// <param name="config"></param>
    /// <param name="references"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    (string badge, string history) Process(IDictionary<object, object?> config, IMarkdownReferences references, IModuleContext context);

    /// <summary>
    ///     The name of the section
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     The configuration key, if you expect to get configuration from the yaml block.
    /// </summary>
    string ConfigKey { get; }
}
