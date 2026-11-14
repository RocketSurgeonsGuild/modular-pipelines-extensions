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
#:package Joinery.Dovetail.ModularPipelines@0.5.3
#:package Specular@0.4.1

using Build;
using Joinery.Dovetail;
using ModularPipelines;

await using var pipeline = await Pipeline.CreateBuilder(args).ConfigureDovetail();
await pipeline.RunAsync();
```

`Joinery.Dovetail.ModularPipelines` and `Specular` have to be direct references: their generators write `ConfigureDovetail` and the
scan results into the build host, and they do not flow through the library. `ConfigureDovetail` is generated into the build host's
root namespace (`Build` for `Build.cs`).

The library's joints:

| Joint                       | What it does                                                                                                              |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| `ModulesJoint`              | Adds every module class of the build host and its references, as Specular found them.                                     |
| `ServiceRegistrationsJoint` | Registers the classes marked `[ServiceRegistration]` (the settings classes) through Specular.                             |
| `RunHistoryJoint`           | Keeps run history under the repository and estimates module durations from it (see below).                                |

**Settings files.** Dovetail reads a file-based build host's settings files from the folder of its `.cs` file (`build/` for
`build/Build.cs`), in place, so a change applies without a rebuild: `appsettings.*`, `appsettings.{Environment}.*` and
`appsettings.local.*`, in JSON and YAML. Each format is layered on its own, so a `.local` file overrides the files of its own format;
environment variables and the command line win over all of them. To read them from another folder, as a build host that is a project
needs to, set `Dovetail:ConfigurationDirectory` (or the `Dovetail__ConfigurationDirectory` environment variable); a relative path is
resolved against the current directory.

**Leaving modules out.** Mark a module `[ExcludeModule]` to keep it out of the scan; it still runs when something adds it, such as
`builder.AddModule<T>()` or another module's dependency. To leave out modules you do not own, put a `ModuleDelegate` in the context,
`ConfigureDovetail(z => z.Set<ModuleDelegate>(type => type != typeof(DocsModule)))`, or leave the `ModulesJoint` out with
`z.ExceptJoint(typeof(ModulesJoint))` and add the modules yourself. ModularPipelines' own `--skip-module` still works at run time.

**MP0013.** ModularPipelines' analyzer warns (MP0013) about a module it does not see registered, and it cannot see the modules
`ModulesJoint` adds. The package turns MP0013 off for the projects that reference it, through a `.globalconfig` at the lowest level,
so a consumer's own `.globalconfig` or `.editorconfig` can turn it back on.

**Dependency cleanup.** `RemoveUnusedDependenciesModule` removes `PackageVersion` entries that no project or `#:package` directive
references, and `PackageReference` entries a `GlobalPackageReference` covers. It rewrites the repository's files on a local build, and a
version that only pins a transitive package looks unused to it, so it is off unless `RemoveUnusedDependencies: true` is set. It never
runs on CI, and the files keep their encoding, byte-order mark and XML declaration.

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

`TestSolutionModule` runs one `dotnet test` per test project, each a sub module of its own, several at once, after the solution is built.
Each project writes its TRX report and cobertura coverage into its own folder (`artifacts/logs/tests/<assembly>`). A failing project
does not stop the others; the module fails at the end and lists every project that failed.

The projects are queued longest first by the median of their last runs (kept in `.modularpipelines/estimates`, failed runs
included), and each slot takes its next project when it is free, so a wrong estimate only shifts work between the slots. With more
than one slot, the first can be a fast lane that takes the shortest project left, so the quick projects report early and run alongside
the long ones while the other slots take the longest. The estimates decide: the fast lane is used when the run finishes no later with it
than without, since it can leave a medium project to start late. Projects with no previous run go first, since one of them may be the
longest. The log shows the order, whether there is a fast lane, each project's estimate and the run's estimated duration.

```yaml
EnableTests: true
TestMode: PerProject # or Solution: one dotnet test over the solution
TestParallelism: 4 # test projects at once; the processor count by default
ExcludeTests: # globs relative to the root: built and packed, but left out of the test run
    - test/Slow.Tests/**
```

`ExcludeTests` keeps suites a workflow runs in jobs of their own out of the pipeline's test run, in either mode (`Solution` runs a
solution filter without them). A pattern that matches no project is logged as a warning. From the environment, it is
`ExcludeTests__0=test/Slow.Tests/**`.

A multi-targeted test project runs one sub module per target framework (`dotnet test --framework`), named `<assembly> [<framework>]`,
each with its own history, slot and results folder (`artifacts/logs/tests/<assembly>/<framework>`), so the long framework starts first
and `TestParallelism` counts test hosts rather than projects. The rest of the settings spread or trim the run:

```yaml
TestPerFramework: true # false runs every framework of a project in one dotnet test
TestFrameworks: [net11.0] # only these frameworks; a project targeting none of them is left out
CoverageFrameworks: [net11.0] # only these collect coverage; one framework's coverage is usually enough
TestOrder: # start these first while they have no previous run, such as on a first run or a new project
    - test/Sdk.Tests/**
TestShard: 1/2 # this job's part of the run; every sub module goes to one part by its place in path order
```

`TestShard` splits without history, so jobs that restore different history still agree on the split. Give each shard its own run history
cache key (for example `modularpipelines-${{ github.workflow }}-main-shard-1-...`), since each saves only its own sub modules. The log
shows each sub module as it starts (with its estimate) and ends, and a table of all of them, longest first; on GitHub Actions that
table also goes to the job summary.

`SolutionProjectsModule` tells `TestSolutionModule` which projects are test projects, what their assemblies are called and which frameworks they target. It evaluates
every project of the solution in one MSBuild process, alongside the build. In `Solution` mode, which does not need the list, it returns an
empty one without evaluating anything (it is not skipped: that would skip the test run with it).
The fast lane is only used once more projects than slots have a previous run: without history, both estimates are zero and say nothing.

`GenerateCodeCoverageReportsModule` turns the cobertura files `TestSolutionModule` collects into an HTML report, summaries and SVG
coverage badges, all under `artifacts/logs/coverage`. `DocsModule` builds the docs as soon as the solution is built, alongside the
tests; when both are in the pipeline, `CopyCoverageBadgesModule` then copies the badges into the published docs site under
`coverage-badges/`, so a docs page can reference them (for example `![coverage](coverage-badges/badge_combined.svg)`)
and they end up on GitHub Pages alongside the rest of the docs.

`Docs:AfterTests: true` makes the docs build wait for the tests instead of running alongside them: on a small runner a heavy docs
build can slow the longest test project by about as much as running alongside saves.

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
