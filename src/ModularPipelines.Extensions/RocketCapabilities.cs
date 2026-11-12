namespace Rocket.Surgery.ModularPipelines.Extensions;

/// <summary>
///     Capabilities the Rocket Surgery modules require with <c>[RequiresCapability]</c>.
/// </summary>
public static class RocketCapabilities
{
    /// <summary>
    ///     Advertised by the process that sees the whole run: the master in a distributed run, and the only process otherwise. Modules
    ///     that read every other module's output (pack, coverage reports, docs, readme) require it so they never land on a worker.
    /// </summary>
    public const string Coordinator = "rocket-coordinator";
}
