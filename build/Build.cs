#:package Microsoft.VisualStudio.SolutionPersistence
#:package ModularPipelines.Analyzers
#:package ModularPipelines
#:package ModularPipelines.DotNet
#:package ModularPipelines.Git
#:package ModularPipelines.GitHub
#:package Rocket.Surgery.Conventions
#:package Rocket.Surgery.Conventions.Configuration.Yaml
#:package Rocket.Surgery.DependencyInjection.Extensions
#:package Sourcy.Git
#:package Sourcy.DotNet
#:package Rocket.Surgery.MyAssembly
#:project ../src/ModularPipelines.Extensions/Rocket.Surgery.ModularPipelines.Extensions.csproj
#:property ImportConventions=true
#:property JsonSerializerIsReflectionEnabledByDefault=true

using Build;
using ModularPipelines;
using ModularPipelines.Plugins;
using Rocket.Surgery.Conventions;
using Rocket.Surgery.ModularPipelines.Extensions;

var pipelineBuilder = Pipeline.CreateBuilder(args);
PluginRegistry.Register(new ConventionsPlugin(ConventionContextBuilder.Create(Imports.Instance)
.AddIfMissing(nameof(MyAssembly.Project.BuildScriptsRoot), MyAssembly.Project.BuildScriptsRoot)));
await pipelineBuilder.Build().RunAsync();
