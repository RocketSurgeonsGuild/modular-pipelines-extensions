using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rocket.Surgery.ModularPipelines.Extensions.Analyzers;

namespace Rocket.Surgery.ModularPipelines.Extensions.Tests;

public class TestShardGeneratorTests
{
    [Test]
    [Arguments("0")]
    [Arguments("1")]
    [Arguments("3")]
    public async Task Generates_one_module_per_shard_from_two(string shards)
    {
        var result = Run(shards, out _);

        await Verify(string.Concat(result.GeneratedTrees.Select(tree => tree.ToString())), "cs").UseParameters(shards);
    }

    [Test]
    public async Task Generates_nothing_without_the_property()
    {
        var result = Run(null, out _);

        await Assert.That(result.GeneratedTrees).IsEmpty();
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("lots")]
    [Arguments("-2")]
    [Arguments("65")]
    public async Task Warns_about_an_invalid_shard_count(string shards)
    {
        var result = Run(shards, out _);

        await Assert.That(result.GeneratedTrees).IsEmpty();
        await Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id)).IsEquivalentTo(["RSMP0001"]);
    }

    [Test]
    public async Task Generated_shards_compile_against_the_library()
    {
        Run("3", out var compilation);

        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.ToString());
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Generates_nothing_without_the_library()
    {
        var compilation = CSharpCompilation.Create("BuildHost", [CSharpSyntaxTree.ParseText("class Program;", ParseOptions)], PlatformReferences());
        var driver = CSharpGeneratorDriver.Create(
            [new TestShardGenerator().AsSourceGenerator()],
            optionsProvider: new GlobalOptionsProvider("4")
        );

        var result = driver.RunGenerators(compilation).GetRunResult();

        await Assert.That(result.GeneratedTrees).IsEmpty();
    }

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    private static GeneratorDriverRunResult Run(string? shards, out Compilation output)
    {
        var compilation = CSharpCompilation.Create(
            "BuildHost",
            [CSharpSyntaxTree.ParseText("class Program { static void Main() { } }", ParseOptions)],
            LoadedReferences(),
            new(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable)
        );
        var driver = CSharpGeneratorDriver.Create(
            [new TestShardGenerator().AsSourceGenerator()],
            optionsProvider: new GlobalOptionsProvider(shards),
            parseOptions: ParseOptions
        );

        return driver.RunGeneratorsAndUpdateCompilation(compilation, out output, out _).GetRunResult();
    }

    // Everything the test host loads: the framework, the library and ModularPipelines.
    private static IEnumerable<MetadataReference> LoadedReferences() =>
        ( (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")! )
       .Split(Path.PathSeparator)
       .Select(path => MetadataReference.CreateFromFile(path));

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        Directory.EnumerateFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
                 .Where(path => !path.Contains("Native", StringComparison.Ordinal))
                 .Select(path => MetadataReference.CreateFromFile(path));

    private sealed class GlobalOptionsProvider(string? shards) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(
            shards is null ? [] : ImmutableDictionary<string, string>.Empty.Add("build_property.RocketTestShards", shards)
        );

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;
    }

    private sealed class Options(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public static readonly Options Empty = new([]);

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
