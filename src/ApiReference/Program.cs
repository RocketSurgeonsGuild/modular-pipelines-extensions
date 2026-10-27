using System.CommandLine;
using Rocket.Surgery.ApiReference;

var solutionOption = new Option<FileInfo>("--solution")
{
    Required = true,
    Description = "The solution (.sln or .slnx) whose packable projects to document. It must already be built.",
};
var configurationOption = new Option<string>("--configuration", "-c")
{
    DefaultValueFactory = _ => "Release",
    Description = "The build configuration to read the assemblies from.",
};
var frameworkOption = new Option<string>("--framework", "-f")
{
    DefaultValueFactory = _ => "net10.0",
    Description = "The target framework to document. Projects that don't target it use their newest platform-neutral one.",
};
var outputOption = new Option<DirectoryInfo>("--output", "-o")
{
    Required = true,
    Description = "The directory to write one <PackageId>.json per package to. JSON files for packages that no longer exist are removed.",
};
var sourceRootOption = new Option<DirectoryInfo?>("--source-root")
{
    Description = "The checkout root that source paths are made relative to. Defaults to the solution's directory.",
};

var rootCommand = new RootCommand("Generates the JSON the docs sites render their C# API reference from, out of a solution's build output.")
{
    solutionOption,
    configurationOption,
    frameworkOption,
    outputOption,
    sourceRootOption,
};

rootCommand.SetAction(
    (parseResult, cancellationToken) =>
    {
        var solution = parseResult.GetRequiredValue(solutionOption);
        return ApiReferenceCommand.RunAsync(
            new(
                solution,
                parseResult.GetRequiredValue(configurationOption),
                parseResult.GetRequiredValue(frameworkOption),
                parseResult.GetRequiredValue(outputOption),
                parseResult.GetValue(sourceRootOption) ?? solution.Directory!
            ),
            cancellationToken
        );
    }
);

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
