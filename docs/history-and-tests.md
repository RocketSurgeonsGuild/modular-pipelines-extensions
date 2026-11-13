# Run history, tests and coverage

The settings below live in the pipeline's configuration, usually `build/appsettings.yaml`. Environment variables override them with `__`
for each level (`History__Enabled=false`, `IncludeTests__0=test/Slow.Tests/**`).

## Run history and estimates

Run history is on by default. ModularPipelines writes a report of every run to `.modularpipelines/run-history`, and the library
estimates each module's duration from it: the median of its last successful runs. The scheduler starts the longest chain of modules
first with these estimates, and the progress display shows them. Run history only records whole modules, so the last duration of
each sub module is kept in `.modularpipelines/estimates`, along with the last run of each test project that the test shards are split
by (below).

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

The `RunHistoryJoint` applies this. A pipeline that does not apply the Dovetail joints calls `builder.AddRocketHistory()`, which also
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

## Test shards

`TestSolution` is one module, so all of its test projects run as sub modules of that one module. Test shards split them between
several modules instead, which ModularPipelines runs concurrently. Set the number of shards in the build host:

```csharp
#:property RocketTestShards=4
```

or in a props file next to it (`<RocketTestShards>4</RocketTestShards>`). Passing it to one run also works:
`dotnet run build/Build.cs -p:RocketTestShards=4`. Unset, `0` or `1` turns shards off. Any other value that is not a whole number
up to 64 is reported as warning `RSMP0001` and turns them off too.

A source generator in the package then writes `TestShard1` to `TestShardN` into the build host. With shards:

- `TestShardPlan` runs once, after the restore. It finds the test projects the way `TestSolution` does
  (`IncludeTests`, `ExcludeTests` and `TestFramework` apply) and splits them between the shards.
- Each shard runs its projects exactly as `TestSolution` does in `PerProject` mode: a sub module per project, `TestParallelism` at a
  time, with each project's TRX and coverage in its own folder under the tests directory, failing at the end with every project that
  failed.
- `TestSolution` skips itself, so no test runs twice.
- `GenerateCodeCoverageReports` waits for every shard and reads all of their coverage.

The shards are added by the `TestShardsJoint`. A build host that does not apply the Dovetail joints calls
`builder.AddRocketTestShards()`.

### How projects are assigned

The split follows the **last run** of each test project. Whenever `TestSolution` or a shard runs a project, the library records how
long its `dotnet test` took, whether it passed and when it finished, in `.modularpipelines/estimates/test-projects.json` (under
`History:EstimatesDirectory`). Each project keeps one entry, replaced by its next run, so the entry is always the newest run of that
project, wherever it ran.

- Projects are placed longest first, each on the shard with the least work so far, by the duration of their last run.
- A project whose last run **failed** still counts with that run's duration: it is its most recent run, and a failing project usually
  fails the same way next time. A run cancelled before the project finished records nothing, so the project keeps its previous entry.
- A project with no last run (new, renamed, or never finished) counts as the median of the other projects' last runs.
- With no last run for any project, projects are dealt round-robin in order of their path.
- Ties are broken by path, so the same last runs and projects always give the same split.

`TestShardPlan` logs the split and what it came from, for example:

```text
Test projects split between 2 shards by the duration of each project's last run:
  shard 1 (estimated 95.2s): test/A.Tests/A.Tests.csproj (last run 95.2s)
  shard 2 (estimated 88.4s): test/B.Tests/B.Tests.csproj (last run 60.1s, failed), test/New.Tests/New.Tests.csproj (no previous run, counted as the median 28.3s)
```

or `Test projects split between 2 shards round-robin by path: no test project has a previous run`. The record is kept only while run
history is on (`History:Enabled`); with it off, every split is round-robin. The module estimates above still use the median of past
runs: they only order the scheduler and the progress display, not the shards.

The split is worked out **once**, by `TestShardPlan`, and reaches the shards as that module's result. The shards never compute it
themselves: durations recorded while they run would give each a different split, which could run a project twice or not at all.
The durations only steer the balance, never which projects run.

Splitting one large test project across shards by test class is not supported yet. TUnit and Microsoft.Testing.Platform can run a
subset with `--treenode-filter "/*/*/<Class>/*"`, but the shards would first have to list each project's classes.

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

## Docs

`DocsModule` passes `GH_API_TOKEN` (or else `GITHUB_TOKEN`, or `GH_TOKEN`) to the docs build as `GH_API_TOKEN`. A failed docs build is
reported without failing the run; set `Docs:IgnoreFailures: false` to fail it.
