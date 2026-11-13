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
#:package Joinery.Dovetail.ModularPipelines@0.5.1
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
| `BuildScriptsRootJoint`     | Points Dovetail's JSON and YAML configuration at the scripts folder, so its settings files are read in place.             |
| `RunHistoryJoint`           | Keeps run history under the repository and estimates module durations from it (see below).                                |

**Settings files.** `BuildScriptsRootJoint` runs before the other configuration joints and points Dovetail's configuration at the
folder in the `BuildScriptsRoot` context value (above, the `BuildScriptsRoot` MSBuild property set in `build/Directory.Build.props` and
exposed through `ProjectProperty`), or else the folder of a file-based build host's `.cs` file. Dovetail's JSON and YAML joints then read
`appsettings.*`, `appsettings.{Environment}.*` and `appsettings.local.*` there, in place, so a change applies without a rebuild. Each
format is layered on its own, so a `.local` file overrides the files of its own format; environment variables and the command line win
over all of them. Set `DovetailEnableYamlConfiguration=false` for a file-based build host with an `appsettings.yaml` next to it: Dovetail
(0.5.1) otherwise generates a joint that requires `Build.yaml` relative to the working directory, while the build copies it to the
output folder.

**Leaving modules out.** Put a `ModuleDelegate` in the context, `ConfigureDovetail(z => z.Set<ModuleDelegate>(type => type !=
typeof(DocsModule)))`, or leave the `ModulesJoint` out with `z.ExceptJoint(typeof(ModulesJoint))` and add the modules yourself.
ModularPipelines' own `--skip-module` still works at run time.

A host that builds the pipeline itself can apply the same joints with
`builder.AddPlugin(new DovetailPipelinePlugin(DovetailContextBuilder.Create()))`.

## Run history, estimates and the module cache

Everything ModularPipelines remembers between runs is kept under `.modularpipelines/` in the repository, instead of the user's
application-data folders, so one CI cache restores all of it. `RunHistoryJoint` sets this up:

| Folder                              | What it holds                                                                                            |
| ----------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `.modularpipelines/run-history`     | A [report of every run](https://modularpipelines.tomlonghurst.dev/docs/how-to/run-reports), with each module's change from the last successful run. |
| `.modularpipelines/estimates`       | The last duration of each module and sub module.                                                         |
| `.modularpipelines/module-cache`    | The [module cache](https://modularpipelines.tomlonghurst.dev/docs/how-to/module-caching).                |

- **A stable pipeline identity.** Runs are grouped by `GITHUB_REPOSITORY/GITHUB_WORKFLOW` on GitHub Actions, or by the name of the root
  folder elsewhere. ModularPipelines would otherwise hash the module list, so adding a module would start a new history.
- **Estimates from history.** Each module's estimated duration is the median of its last successful runs, or else its last duration.
  The scheduler starts the longest chain first with these estimates, and the progress display shows them.
- **The module cache is on.** It restores only the modules that declare what their result depends on: `[CacheInputs]` file patterns
  (relative to the repository root), `WithCacheKeyPart`, `WithCacheEnvironmentVariable` or `WithCacheAssemblyVersionKey`. Every other
  module runs as before. Declare the files a restored module must recreate with `[ProducesArtifact]`; `--no-cache` bypasses the cache
  for one run.

The settings live in the `History` section, usually in `build/appsettings.yaml`. `RocketHistoryOptions` declares it as Dovetail
[managed configuration](https://rocketsurgeonsguild.github.io/Dovetail/concepts/managed-configuration/), so editors get its schema and
modules can read it through `IOptions<RocketHistoryOptions>`:

```yaml
History:
    Enabled: true # false turns run history off; the estimates are still kept
    Directory: .modularpipelines/run-history # relative to the repository root
    EstimatesDirectory: .modularpipelines/estimates
    Retention: 50 # runs kept for this pipeline
    Samples: 10 # runs an estimate is the median of
    Identity: my-org/my-repo # defaults to GITHUB_REPOSITORY/GITHUB_WORKFLOW, or the root folder's name
    ModuleCache:
        Enabled: true # false leaves the module cache out
        Directory: .modularpipelines/module-cache
```

`.modularpipelines/` belongs in `.gitignore`. A CI runner starts without it, so restore it before the build. Saving it only from the
default branch keeps one history, which every pull request restores:

```yaml
- uses: actions/cache/restore@v6
  with:
      path: .modularpipelines
      key: modularpipelines-${{ github.workflow }}-main-${{ github.run_id }}-${{ github.run_attempt }}
      restore-keys: |
          modularpipelines-${{ github.workflow }}-main-
- run: mise run build
- if: always() && github.ref == 'refs/heads/main'
  uses: actions/cache/save@v6
  with:
      path: .modularpipelines
      key: modularpipelines-${{ github.workflow }}-main-${{ github.run_id }}-${{ github.run_attempt }}
```

## Tests

`TestSolution` runs one `dotnet test` per test project, each a sub module of its own, several at once, after the solution is built.
Each project writes its TRX report and cobertura coverage into its own folder (`artifacts/logs/tests/<assembly>`). A failing project
does not stop the others; the module fails at the end and lists every project that failed.

The projects start in the order that should finish soonest: each free slot takes the next project, and they come longest first by the
median of their last runs (kept in `.modularpipelines/estimates`, failed runs included). Projects with no previous run go first, since
one of them may be the longest. The log shows the order, each project's estimate and the run's estimated duration.

```yaml
EnableTests: true
TestMode: PerProject # or Solution: one dotnet test over the solution
TestParallelism: 4 # test projects at once; half the processors by default
ExcludeTests: # projects left out of the test run (globs relative to the root); they still build and pack
    - test/Slow.Tests/**
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
