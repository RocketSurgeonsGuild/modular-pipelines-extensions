using Rocket.Surgery.ModularPipelines.Extensions.Modules;

namespace Rocket.Surgery.ModularPipelines.Extensions.Tests;

public class SolutionProjectsTests
{
    [Test]
    public async Task Reads_each_projects_info_from_the_target_result()
    {
        // The shape `dotnet msbuild -getTargetResult` prints, trimmed to the metadata the module reads.
        const string json = """
            {
              "TargetResults": {
                "Collect": {
                  "Result": "Success",
                  "Items": [
                    { "Identity": "/repo/src/Lib/Lib.csproj", "AssemblyName": "Rocket.Lib", "IsTestProject": "", "TargetFramework": "net11.0", "TargetFrameworks": "" },
                    { "Identity": "/repo/test/Multi/Multi.csproj", "AssemblyName": "Multi", "IsTestProject": "", "TargetFramework": "", "TargetFrameworks": "net10.0; net11.0" },
                    { "Identity": "/repo/test/Single/Single.csproj", "AssemblyName": "Single", "IsTestProject": "true", "TargetFramework": "net11.0", "TargetFrameworks": "" }
                  ]
                }
              }
            }
            """;

        var info = SolutionProjectsModule.Parse(json);

        await Assert.That(info.Count).IsEqualTo(3);
        await Assert.That(info[Path.GetFullPath("/repo/src/Lib/Lib.csproj")]).IsEqualTo(new SolutionProjectsModule.ProjectInfo("Rocket.Lib", "", null));
        // Only a multi-targeted project's outer build needs asking again, with its first framework chosen.
        await Assert.That(info[Path.GetFullPath("/repo/test/Multi/Multi.csproj")]).IsEqualTo(new SolutionProjectsModule.ProjectInfo("Multi", "", "net10.0"));
        await Assert.That(info[Path.GetFullPath("/repo/test/Single/Single.csproj")]).IsEqualTo(new SolutionProjectsModule.ProjectInfo("Single", "true", null));
    }

    [Test]
    public async Task Reads_nothing_when_no_project_answered()
    {
        await Assert.That(SolutionProjectsModule.Parse("""{ "TargetResults": { "Collect": { "Result": "Success", "Items": [] } } }""")).IsEmpty();
        await Assert.That(SolutionProjectsModule.Parse("""{ "TargetResults": {} }""")).IsEmpty();
    }
}
