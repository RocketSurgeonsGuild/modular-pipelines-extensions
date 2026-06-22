// ReSharper disable once CheckNamespace
using Rocket.Surgery.ModularPipelines.Extensions.Support;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Rocket.Surgery.Conventions;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
///     Helper method for working with <see cref="ConventionContextBuilder" />
/// </summary>
[PublicAPI]
public static class PipelineBuilderConventionExtensions
{
    /// <summary>
    ///     Configure the hosting delegate to the convention scanner
    /// </summary>
    /// <param name="container">The container.</param>
    /// <param name="delegate">The delegate.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="category">The category.</param>
    /// <returns>ConventionContextBuilder.</returns>
    public static ConventionContextBuilder ConfigurePipelineBuilder(
        this ConventionContextBuilder container,
        PipelineBuilderConvention @delegate,
        int priority = 0,
        ConventionCategory? category = null
    )
    {
        ArgumentNullException.ThrowIfNull(container);

        container.AppendDelegate(@delegate, priority, category);
        return container;
    }

    /// <summary>
    ///     Configure the hosting delegate to the convention scanner
    /// </summary>
    /// <param name="container">The container.</param>
    /// <param name="delegate">The delegate.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="category">The category.</param>
    /// <returns>ConventionContextBuilder.</returns>
    public static ConventionContextBuilder ConfigurePipelineBuilder(
        this ConventionContextBuilder container,
        PipelineBuilderAsyncConvention @delegate,
        int priority = 0,
        ConventionCategory? category = null
    )
    {
        ArgumentNullException.ThrowIfNull(container);

        container.AppendDelegate(@delegate, priority, category);
        return container;
    }

    /// <summary>
    ///     Configure the hosting delegate to the convention scanner
    /// </summary>
    /// <param name="container">The container.</param>
    /// <param name="delegate">The delegate.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="category">The category.</param>
    /// <returns>ConventionContextBuilder.</returns>
    public static ConventionContextBuilder ConfigurePipelineBuilder(
        this ConventionContextBuilder container,
        Action<PipelineBuilder> @delegate,
        int priority = 0,
        ConventionCategory? category = null
    )
    {
        ArgumentNullException.ThrowIfNull(container);

        container.AppendDelegate(new PipelineBuilderConvention((_, builder) => @delegate(builder)), priority, category);
        return container;
    }

    /// <summary>
    ///     Configure the hosting delegate to the convention scanner
    /// </summary>
    /// <param name="container">The container.</param>
    /// <param name="delegate">The delegate.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="category">The category.</param>
    /// <returns>ConventionContextBuilder.</returns>
    public static ConventionContextBuilder ConfigurePipelineBuilder(
        this ConventionContextBuilder container,
        Func<PipelineBuilder, ValueTask> @delegate,
        int priority = 0,
        ConventionCategory? category = null
    )
    {
        ArgumentNullException.ThrowIfNull(container);

        container.AppendDelegate(new PipelineBuilderAsyncConvention((_, builder, _) => @delegate(builder)), priority, category);
        return container;
    }

    /// <summary>
    ///     Configure the hosting delegate to the convention scanner
    /// </summary>
    /// <param name="container">The container.</param>
    /// <param name="delegate">The delegate.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="category">The category.</param>
    /// <returns>ConventionContextBuilder.</returns>
    public static ConventionContextBuilder ConfigurePipelineBuilder(
        this ConventionContextBuilder container,
        Func<PipelineBuilder, CancellationToken, ValueTask> @delegate,
        int priority = 0,
        ConventionCategory? category = null
    )
    {
        ArgumentNullException.ThrowIfNull(container);

        container.AppendDelegate(
            new PipelineBuilderAsyncConvention((_, builder, cancellationToken) => @delegate(builder, cancellationToken)),
            priority,
            category
        );
        return container;
    }
}
