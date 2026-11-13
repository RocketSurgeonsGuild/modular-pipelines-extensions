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

| Joint                       | What it does                                                                                                              |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| `ModulesJoint`              | Adds every module class of the build host and its references, as Specular found them.                                     |
| `ServiceRegistrationsJoint` | Registers the classes marked `[ServiceRegistration]` (the settings classes) through Specular.                             |
| `BuildScriptsRootJoint`     | Reads `appsettings.json`/`.yaml`/`.yml`, `appsettings.{Environment}.*` and `appsettings.local.*` from the scripts folder. |
| `RunHistoryJoint`           | Keeps run history under the repository and estimates module durations from it (see below).                                |

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

## Run history

ModularPipelines writes a report of every run to `.modularpipelines/run-history` (see
[run reports](https://modularpipelines.tomlonghurst.dev/docs/how-to/run-reports)), and shows each module's change from the previous
successful run. `RunHistoryJoint` (or `builder.AddRocketHistory()` without the joints) adds:

- **A stable pipeline identity.** Runs are grouped by `GITHUB_REPOSITORY/GITHUB_WORKFLOW` on GitHub Actions, or by the name of the root
  folder elsewhere. ModularPipelines would otherwise hash the module list, so adding a module would start a new history.
- **Estimates from history.** Each module's estimated duration is the median of its last successful runs. The scheduler starts the
  longest chain first with these estimates, and the progress display shows them. Sub modules, and modules with no successful run yet,
  use their last duration, kept in `.modularpipelines/estimates` (ModularPipelines would keep it in the user's application-data folder,
  which a CI cache does not restore).
- **An opt-in module cache** that skips modules whose declared `[CacheInputs]` did not change.

The settings live in the `History` section, usually in `build/appsettings.yaml`:

```yaml
History:
    Enabled: true # false turns run history off
    Directory: .modularpipelines/run-history # relative to the repository root
    EstimatesDirectory: .modularpipelines/estimates
    Retention: 30 # runs kept for this pipeline
    Samples: 10 # runs an estimate is the median of
    Identity: my-org/my-repo # defaults to GITHUB_REPOSITORY/GITHUB_WORKFLOW, or the root folder's name
    ModuleCache:
        Enabled: false
        Directory: .modularpipelines/module-cache
```

`.modularpipelines/` belongs in `.gitignore`. A CI runner starts without it, so cache it between runs:

```yaml
- uses: actions/cache@v6
  with:
      path: .modularpipelines
      key: modularpipelines-${{ github.workflow }}-${{ github.ref_name }}-${{ github.run_id }}-${{ github.run_attempt }}
      restore-keys: |
          modularpipelines-${{ github.workflow }}-${{ github.ref_name }}-
          modularpipelines-${{ github.workflow }}-main-
```

| Package                                    | NuGet                                                                                     |
| ------------------------------------------ | ----------------------------------------------------------------------------------------- |
| Rocket.Surgery.ModularPipelines.Extensions | [![nuget-version-modular-pipelines-extensions-badge]][nuget-modular-pipelines-extensions] |

<!-- nuget packages -->

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
