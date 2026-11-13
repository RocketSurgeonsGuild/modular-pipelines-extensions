# Modular Pipelines Extensions

# Status

<!-- badges -->

[![github-license-badge]][github-license]

<!-- badges -->

<!-- history badges -->

| GitHub Actions            |
| ------------------------- |
| [![github-badge]][github] |

<!-- history badges -->

<!-- nuget packages -->

| Package                                    | NuGet                                                                                     |
| ------------------------------------------ | ----------------------------------------------------------------------------------------- |
| Rocket.Surgery.ModularPipelines.Extensions | [![nuget-version-modular-pipelines-extensions-badge]][nuget-modular-pipelines-extensions] |

<!-- nuget packages -->

# The build host

The library ships its setup as [Dovetail](https://github.com/RocketSurgeonsGuild/Dovetail) joints and finds its modules and services
with [Specular](https://github.com/RocketSurgeonsGuild/Specular) when the build host is compiled, so nothing is scanned at run time. A
file-based build host references both next to the library and applies the joints with `ConfigureDovetail()`:

```csharp
#:package Rocket.Surgery.ModularPipelines.Extensions@<version>
#:package Joinery.Dovetail.ModularPipelines@0.5.0
#:package Specular@0.4.1

using Build;
using Joinery.Dovetail;
using ModularPipelines;
using Rocket.Surgery.ModularPipelines.Extensions.Joints;

await using var pipeline = await Pipeline
                                .CreateBuilder(args)
                                .ConfigureDovetail(z => z.Set(BuildScriptsRootJoint.BuildScriptsRootKey, AssemblyInfo.Project.BuildScriptsRoot));
await pipeline.RunAsync();
```

`Joinery.Dovetail.ModularPipelines` and `Specular` have to be direct references: their generators write `ConfigureDovetail` and the
scan results into the build host, and they do not flow through the library. `ConfigureDovetail` is generated into the build host's
root namespace (`Build` for `Build.cs`). `ConfigureDovetail()` without arguments works as well; see the settings files below.

The library's joints:

| Joint                       | What it does                                                                                                     |
| --------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `ModulesJoint`              | Adds every module class of the build host and its references, as Specular found them (not the test shards).    |
| `ServiceRegistrationsJoint` | Registers the classes marked `[ServiceRegistration]` (the settings classes) through Specular.                    |
| `BuildScriptsRootJoint`     | Reads `appsettings.json`/`.yaml`/`.yml`, `appsettings.{Environment}.*` and `appsettings.local.*` from the scripts folder. |
| `RunHistoryJoint`           | Applies `AddRocketHistory()`.                                                                                    |
| `TestShardsJoint`           | Applies `AddRocketTestShards()`.                                                                                 |

**Settings files.** `BuildScriptsRootJoint` reads them from the folder in the `BuildScriptsRoot` context value (above, the
`BuildScriptsRoot` MSBuild property set in `build/Directory.Build.props` and exposed through `ProjectProperty`), or else from the folder
of a file-based build host's `.cs` file. They are read in place, so a change applies without a rebuild. JSON comes before YAML and
`appsettings.local.*` last; environment variables and the command line win over all of them. Set
`DovetailEnableYamlConfiguration=false` for a file-based build host with an `appsettings.yaml` next to it: otherwise Dovetail adds a
joint that requires the file in the output folder, where `dotnet run` does not copy it.

**Leaving modules out.** Put a `ModuleDelegate` in the context, `ConfigureDovetail(z => z.Set<ModuleDelegate>(type => type !=
typeof(DocsModule)))`, or leave the `ModulesJoint` out with `z.ExceptJoint(typeof(ModulesJoint))` and add the modules yourself.
ModularPipelines' own `--skip-module` still works at run time.

A host that builds the pipeline itself can apply the same joints with
`builder.AddPlugin(new DovetailPipelinePlugin(DovetailContextBuilder.Create()))`.

# Whats next?

TBD

<!-- generated references -->

[github-license]: https://github.com/RocketSurgeonsGuild/modular-pipelines-extensions/blob/main/LICENSE
[github-license-badge]: https://img.shields.io/github/license/RocketSurgeonsGuild/modular-pipelines-extensions.svg?style=flat 'License'
[github]: https://github.com/RocketSurgeonsGuild/modular-pipelines-extensions/actions?query=workflow%3Aci
[github-badge]: https://img.shields.io/github/actions/workflow/status/RocketSurgeonsGuild/modular-pipelines-extensions/ci.yml?branch=main&label=github&logo=github&color=b845fc&logoColor=b845fc&style=flat 'GitHub Actions Status'
[nuget-modular-pipelines-extensions]: https://www.nuget.org/packages/Rocket.Surgery.ModularPipelines.Extensions/
[nuget-version-modular-pipelines-extensions-badge]: https://img.shields.io/nuget/v/Rocket.Surgery.ModularPipelines.Extensions.svg?color=004880&logo=nuget&style=flat-square 'NuGet Version'

<!-- nuke-data
github:
  owner: RocketSurgeonsGuild
  repository: modular-pipelines-extensions
-->
