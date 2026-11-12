using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModularPipelines.Caching;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Test shards: <c>TestShard1</c> to <c>TestShardN</c>, modules the library's source generator writes into the build host when it
///     sets the <c>RocketTestShards</c> MSBuild property to more than one. Each runs a share of the test projects, so a distributed run
///     can hand them to different workers, which it cannot do with the single <see cref="TestSolution" /> module.
/// </summary>
[PublicAPI]
public static class TestShards
{
    /// <summary>The artifact that carries a shard's results from a worker to the coordinator.</summary>
    public const string ResultsArtifact = "rocket-test-shard-results";

    /// <summary>
    ///     The folder, relative to the pipeline's working directory, under which each shard stages the results it hands back
    ///     (<c>shard-1</c>, <c>shard-2</c> and so on). Outside a worker a shard's folder stays empty.
    /// </summary>
    public const string ResultsArtifactDirectory = ".modularpipelines/test-shards";

    private static ImmutableArray<TestShardDescriptor>? _registration;

    /// <summary>The staging folder of a shard's results artifact.</summary>
    /// <param name="options">The module cache options, whose working directory artifact paths are relative to.</param>
    /// <param name="number">The shard's number, from one.</param>
    public static FolderPath GetResultsArtifactFolder(ModuleCacheOptions options, int number)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(Path.GetFullPath($"{ResultsArtifactDirectory}/shard-{number}", options.WorkingDirectory));
    }

    /// <summary>Called by the generated code when the build host starts; not meant to be called directly.</summary>
    /// <param name="shards">The shards, in order: the first is shard one.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Register(TestShardDescriptor[] shards)
    {
        ArgumentNullException.ThrowIfNull(shards);
        _registration = [.. shards];
    }

    /// <summary>Describes a generated shard; called by the generated code.</summary>
    /// <typeparam name="TShard">The shard module.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TestShardDescriptor Describe<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TShard>()
        where TShard : TestShardModule =>
        new(
            typeof(TShard),
            static builder => builder.AddModule<TShard>(),
            static async context => context.GetModuleIfRegistered<TShard>() is { } shard ? ( await shard ).ValueOrDefault : null
        );

    /// <summary>
    ///     Adds the generated test shards and the <see cref="TestShardPlan" /> that splits the test projects between them, and turns
    ///     <see cref="TestSolution" /> off. Without generated shards (<c>RocketTestShards</c> unset or one) it only records that there are
    ///     none. The <c>TestShardsJoint</c> calls it for a build host that applies Dovetail's joints.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <returns>The same builder.</returns>
    public static PipelineBuilder AddRocketTestShards(this PipelineBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(TestShardSet))) return builder;

        var shards = new TestShardSet(_registration ?? []);
        builder.Services.AddSingleton(shards);
        if (!shards.IsEnabled) return builder;

        builder.Services.TryAddSingleton<TestShardServices>();
        builder.AddModule<TestShardPlan>();
        foreach (var shard in shards.Shards)
        {
            shard.AddModule(builder);
        }

        return builder;
    }
}

/// <summary>A generated test shard: its type, how to add it, and how to read its result.</summary>
/// <param name="ModuleType">The shard module type.</param>
/// <param name="AddModule">Adds it to a pipeline.</param>
/// <param name="GetResultAsync">Reads its result from another module.</param>
[PublicAPI]
public sealed record TestShardDescriptor(
    Type ModuleType,
    Action<PipelineBuilder> AddModule,
    Func<IModuleContext, Task<TestSolution.Result?>> GetResultAsync
);

/// <summary>The generated test shards of this build host.</summary>
/// <param name="shards">The shards, in order: the first is shard one.</param>
[PublicAPI]
public sealed class TestShardSet(IReadOnlyList<TestShardDescriptor> shards)
{
    /// <summary>The shards, in order: the first is shard one.</summary>
    public IReadOnlyList<TestShardDescriptor> Shards { get; } = shards;

    /// <summary>The shard module types, in order: the first is shard one.</summary>
    public IEnumerable<Type> ModuleTypes => Shards.Select(shard => shard.ModuleType);

    /// <summary>How many shards there are.</summary>
    public int Count => Shards.Count;

    /// <summary>Whether the tests run in shards, which takes more than one.</summary>
    public bool IsEnabled => Count > 1;
}
