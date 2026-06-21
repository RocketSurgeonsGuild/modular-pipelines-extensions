using ModularPipelines.Options;
using File = ModularPipelines.FileSystem.File;

namespace build.library;

internal static class MSBuildExtensions
{
    public static T BinlogTo<T>(this T options, IPipelineContext context, File log) where T : CommandLineToolOptions
    {
        var args = options.Arguments?.ToList() ?? [];
        args.Add($"/bl:\"{log}\"");
        args.Add("/fileLogger");
        args.Add($"/fileloggerparameters:LogFile=\"{log.Folder}/{log.NameWithoutExtension}.log\"");

        return options with { Arguments = args };
    }
}
