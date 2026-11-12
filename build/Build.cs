#:package Microsoft.VisualStudio.SolutionPersistence
#:package Sourcy.Git
#:package Sourcy.DotNet
#:package Indago
#:project ../src/ModularPipelines.Extensions/Rocket.Surgery.ModularPipelines.Extensions.csproj
#:property ImportConventions=true
#:property JsonSerializerIsReflectionEnabledByDefault=true

using Build;
using ModularPipelines;
using Rocket.Surgery.ModularPipelines.Extensions;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

var builder = Pipeline.CreateBuilder(args);
builder.AddPlugin(new ConventionsPlugin(
    ConventionContextBuilder.Create(Imports.Instance)
        .AddIfMissing(nameof(AssemblyInfo.Project.BuildScriptsRoot), AssemblyInfo.Project.BuildScriptsRoot),
        x => x != typeof(TestSolution)
        ));
await builder.RunAsync();
