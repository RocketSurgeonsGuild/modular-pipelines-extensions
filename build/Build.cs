#:package Microsoft.VisualStudio.SolutionPersistence
#:package Sourcy.Git
#:package Sourcy.DotNet
#:package Joinery.Dovetail.ModularPipelines
#:package Specular
#:project ../src/ModularPipelines.Extensions/Rocket.Surgery.ModularPipelines.Extensions.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true

using Build;
using Joinery.Dovetail;
using ModularPipelines;
using Rocket.Surgery.ModularPipelines.Extensions.Joints;

var builder = Pipeline.CreateBuilder(args);
await using var pipeline = await builder.ConfigureDovetail(
    z => z.Set(BuildScriptsRootJoint.BuildScriptsRootKey, AssemblyInfo.Project.BuildScriptsRoot)
);
await pipeline.RunAsync();
