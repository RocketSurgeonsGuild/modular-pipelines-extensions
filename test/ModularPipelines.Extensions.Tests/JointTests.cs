using Joinery.Dovetail;
using Joinery.Dovetail.Joints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.ModularPipelines.Extensions.Joints;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;
using Rocket.Surgery.ModularPipelines.Extensions.Readme;

namespace Rocket.Surgery.ModularPipelines.Extensions.Tests;

public class JointTests
{
    private static readonly Type[] LibraryModules =
    [
        typeof(BuildSolution),
        typeof(DocsModule),
        typeof(DownloadPackageIconModule),
        typeof(GenerateCodeCoverageReports),
        typeof(GitVersionModule),
        typeof(PackSolution),
        typeof(ReadmeUpdater),
        typeof(RemoveUnusedDependenciesModule),
        typeof(RestoreSolution),
        typeof(ShipPublicApisModule),
        typeof(TestSolution),
    ];

    [Test]
    public async Task Adds_every_module_of_the_library()
    {
        await using var pipeline = await Pipeline.CreateBuilder().ConfigureDovetail();

        var plan = await pipeline.PlanAsync();
        // The scan covers the build host too, which here is this test assembly with its own test modules.
        var modules = plan.Modules.Select(module => module.GetType()).Where(type => type.Assembly == typeof(BuildSolution).Assembly).ToArray();

        await Assert.That(modules).IsEquivalentTo(LibraryModules);
    }

    [Test]
    public async Task Leaves_out_the_modules_the_module_delegate_rejects()
    {
        await using var pipeline = await Pipeline
                                        .CreateBuilder()
                                        .ConfigureDovetail(z => z.Set<ModuleDelegate>(type => type != typeof(DocsModule)));

        var plan = await pipeline.PlanAsync();
        var modules = plan.Modules.Select(module => module.GetType()).ToArray();

        await Assert.That(modules).DoesNotContain(typeof(DocsModule));
        await Assert.That(modules).Contains(typeof(BuildSolution));
    }

    [Test]
    public async Task Adds_only_the_modules_of_the_build_host_without_the_modules_joint()
    {
        await using var pipeline = await Pipeline
                                        .CreateBuilder()
                                        .ConfigureDovetail(
                                             z => z
                                                 .ExceptJoint(typeof(ModulesJoint))
                                                 .ConfigurePipelineBuilder((_, builder) => builder.AddModule<GitVersionModule>())
                                         );

        var plan = await pipeline.PlanAsync();

        await Assert.That(plan.Modules.Select(module => module.GetType())).IsEquivalentTo(new[] { typeof(GitVersionModule) });
    }

    [Test]
    public async Task Registers_the_settings_marked_for_service_registration()
    {
        await using var pipeline = await Pipeline.CreateBuilder().ConfigureDovetail();

        await Assert.That(pipeline.Services.GetService<SharedSettings>()).IsNotNull();
        await Assert.That(pipeline.Services.GetService<SolutionSettings>()).IsNotNull();
        await Assert.That(pipeline.Services.GetService<TestSettings>()).IsNotNull();
        await Assert.That(pipeline.Services.GetService<ArtifactSettings>()).IsNotNull();
        await Assert.That(pipeline.Services.GetService<ReadmeSettings>()).IsNotNull();
        await Assert.That(pipeline.Services.GetService<DocsModule.Settings>()).IsNotNull();
        await Assert.That(pipeline.Services.GetServices<ICommandInterceptor>().OfType<RootDirectoryInterceptor>()).IsNotEmpty();
    }

    [Test]
    public async Task Reads_the_settings_files_from_the_build_scripts_root()
    {
        var root = Directory.CreateTempSubdirectory("rocket-build-scripts-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "appsettings.yaml"), "Rocket:\n  FromYaml: yaml\n  Overridden: yaml\n");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "appsettings.local.yaml"), "Rocket:\n  Overridden: local\n");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "appsettings.json"), """{ "Rocket": { "FromJson": "json" } }""");

            await using var pipeline = await Pipeline
                                            .CreateBuilder()
                                            .ConfigureDovetail(z => z.Set(BuildScriptsRootJoint.BuildScriptsRootKey, root.FullName));

            var configuration = pipeline.Services.GetRequiredService<IConfiguration>();
            await Assert.That(configuration["Rocket:FromYaml"]).IsEqualTo("yaml");
            await Assert.That(configuration["Rocket:FromJson"]).IsEqualTo("json");
            await Assert.That(configuration["Rocket:Overridden"]).IsEqualTo("local");
        }
        finally
        {
            root.Delete(true);
        }
    }
}
