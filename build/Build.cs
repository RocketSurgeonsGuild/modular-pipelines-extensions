#:package Microsoft.VisualStudio.SolutionPersistence
#:package Sourcy.Git
#:package Sourcy.DotNet
#:package Indago
#:project ../src/ModularPipelines.Extensions/Rocket.Surgery.ModularPipelines.Extensions.csproj
#:property ImportConventions=true
#:property JsonSerializerIsReflectionEnabledByDefault=true

using Build;
using Microsoft.Extensions.Configuration;
using ModularPipelines;
using Rocket.Surgery.ModularPipelines.Extensions;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

var builder = Pipeline.CreateBuilder(args);
// docs/ holds plain Markdown, not a docs site to build.
builder.Configuration.AddInMemoryCollection([new("Docs:Enabled", "false")]);
builder.AddPlugin(new ConventionsPlugin(
    ConventionContextBuilder.Create(Imports.Instance)
        .AddIfMissing(nameof(MyAssembly.Project.BuildScriptsRoot), MyAssembly.Project.BuildScriptsRoot),
        x => x != typeof(TestSolution)
        ));
await builder.RunAsync();
