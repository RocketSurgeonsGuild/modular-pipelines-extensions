using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

/// <summary>
///     Test shards: <c>TestShard1</c> to <c>TestShardN</c>, modules the library's source generator writes into the build host when it
///     sets the <c>RocketTestShards</c> MSBuild property to more than one. Each runs a share of the test projects as a module of its
///     own, so ModularPipelines runs the shards concurrently, as separate modules, instead of all of them in the one
///     <see cref="TestSolution" /> module.
/// </summary>
[PublicAPI]
public static class TestShards
{
    private static ImmutableArray<TestShardDescriptor>? _registration;

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
        new(typeof(TShard), static builder => builder.AddModule<TShard>());

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

/// <summary>A generated test shard: its type and how to add it.</summary>
/// <param name="ModuleType">The shard module type.</param>
/// <param name="AddModule">Adds it to a pipeline.</param>
[PublicAPI]
public sealed record TestShardDescriptor(Type ModuleType, Action<PipelineBuilder> AddModule);

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
