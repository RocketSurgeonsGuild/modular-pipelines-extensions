using Joinery.Dovetail;
using Joinery.Dovetail.Joints;
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
        typeof(PackSolutionModule),
        typeof(ReadmeUpdaterModule),
        typeof(RemoveUnusedDependenciesModule),
        typeof(RestoreSolutionModule),
        typeof(ShipPublicApisModule),
        typeof(TestSolutionModule),
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
    public async Task Leaves_out_the_modules_marked_to_exclude_themselves()
    {
        await using var pipeline = await Pipeline.CreateBuilder().ConfigureDovetail();

        var plan = await pipeline.PlanAsync();
        var modules = plan.Modules.Select(module => module.GetType()).ToArray();

        await Assert.That(modules).DoesNotContain(typeof(ExcludedModule));
        await Assert.That(modules).Contains(typeof(BuildSolution));
    }

    [Test]
    public async Task Runs_an_excluded_module_that_is_added_on_purpose()
    {
        await using var pipeline = await Pipeline
                                        .CreateBuilder()
                                        .ConfigureDovetail(z => z.ConfigurePipelineBuilder((_, builder) => builder.AddModule<ExcludedModule>()));

        var plan = await pipeline.PlanAsync();

        await Assert.That(plan.Modules.Select(module => module.GetType())).Contains(typeof(ExcludedModule));
    }
}

[ExcludeModule]
public sealed class ExcludedModule : Module<bool>
{
    protected override Task<bool> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken) => Task.FromResult(true);
}
