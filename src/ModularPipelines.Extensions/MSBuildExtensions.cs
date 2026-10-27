using ModularPipelines.Options;
using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions;

internal static class MSBuildExtensions
{
    public static T BinlogTo<T>(this T options, IPipelineContext context, File log) where T : CommandLineToolOptions
    {
        var args = options.Arguments?.ToList() ?? [];
        args.Add($"/bl:\"{log}\"");
        args.Add("/fileLogger");
        // The binary log already records everything. The text log only needs what the console shows: at the file logger's default
        // (detailed) verbosity it reached 2 GB for a solution build and slowed the build down writing it.
        args.Add($"/fileloggerparameters:LogFile=\"{log.Folder}/{log.NameWithoutExtension}.log\";Verbosity=minimal;Summary");

        return options with { Arguments = args };
    }
}
