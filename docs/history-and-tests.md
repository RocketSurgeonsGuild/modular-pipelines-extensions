# Run history, tests and coverage

The settings below live in the pipeline's configuration, usually `build/appsettings.yaml`. Environment variables override them with `__`
for each level (`History__Enabled=false`, `IncludeTests__0=test/Slow.Tests/**`).

## Run history and estimates

Run history is on by default. ModularPipelines writes a report of every run to `.modularpipelines/run-history`, and the library
estimates each module's duration from it: the median of its last successful runs. The scheduler starts the longest chain of modules
first with these estimates, and the progress display shows them. Run history only records whole modules, so the last duration of
each sub module (each test project, below) is kept in `.modularpipelines/estimates`.

```yaml
History:
  Enabled: true # false turns run history off
  Directory: .modularpipelines/run-history # relative to the repository root
  EstimatesDirectory: .modularpipelines/estimates
  Retention: 30 # runs kept for this pipeline
  Samples: 10 # runs an estimate is the median of
  Identity: my-org/my-repo # defaults to GITHUB_REPOSITORY/GITHUB_WORKFLOW, or the root folder's name
  ModuleCache:
    Enabled: false # opt in: skip modules whose declared [CacheInputs] did not change
    Directory: .modularpipelines/module-cache
```

The folders are ignored by git. A CI agent starts empty, so cache them between runs:

```yaml
- uses: actions/cache@v4
  with:
    path: .modularpipelines
    key: mp-history-${{ github.workflow }}-${{ github.ref_name }}-${{ github.run_id }}-${{ github.run_attempt }}
    restore-keys: |
      mp-history-${{ github.workflow }}-${{ github.ref_name }}-
      mp-history-${{ github.workflow }}-main-
```

The `RunHistoryConvention` applies this. A pipeline that does not use the conventions calls `builder.AddRocketHistory()`, which also
takes a function to change the options read from configuration.

## Tests

`TestSolution` runs each test project as a sub module of its own, several at once, after the solution has been built. Each project
writes its TRX report and cobertura coverage into its own folder under the tests directory (`artifacts/logs/tests/<assembly>`). A
failing project does not stop the others; the module fails at the end and lists the projects that failed.

```yaml
EnableTests: true
TestMode: PerProject # or Solution: one dotnet test over the solution, as before
TestParallelism: 4 # test projects at once; half the processors by default
TestFramework: net10.0 # optional: dotnet test --framework
ExcludeTests: # these projects never run here (globs relative to the root)
  - test/Slow.Tests/**
TestsDirectory: artifacts/logs/tests
```

`IncludeTests` and `ExcludeTests` split the tests between CI jobs: the main job excludes a slow suite as above, and another job runs
only it, with the same TRX and coverage layout:

```yaml
IncludeTests: # only these projects
  - test/Slow.Tests/**
```

`TestFramework` splits a multi-targeted suite by framework the same way.

## Coverage

Unless `RunSettings` names a file of your own, the library writes the coverage settings on every run. They collect coverage of the
assemblies of the solution's non-test projects only, never test assemblies or generated code. Add or remove modules with regular
expressions matched against the module's path:

```yaml
CoverageInclude:
  - .*[\\/]MyGenerated\.Runtime\.dll$
CoverageExclude:
  - .*[\\/]Samples?\..*\.dll$
```

`GenerateCodeCoverageReports` reads every `*.cobertura.xml` under the tests directory, and the files matched by `CoverageReports`, such
as coverage downloaded from the jobs that ran other tests:

```yaml
CoverageReports:
  - artifacts/downloaded/**/*.cobertura.xml
```

## Distributed runs

The modules that read or write the whole run (GitVersion, restore, build, pack, docs, readme, public API shipping, dependency cleanup
and the coverage report) require the `rocket-coordinator` capability. Every process that is not a distributed worker advertises it,
so a standalone run behaves as before. `TestSolution` does not require it: on a worker it restores and builds what it tests, and its
results travel back to the coordinator as the `rocket-test-results` artifact, which the coverage report restores into the tests
directory.

## Docs

`DocsModule` passes `GH_API_TOKEN` (or else `GITHUB_TOKEN`, or `GH_TOKEN`) to the docs build as `GH_API_TOKEN`. A failed docs build is
reported without failing the run; set `Docs:IgnoreFailures: false` to fail it.
