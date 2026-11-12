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

## Test shards

`TestSolution` is one module, and ModularPipelines places work by module type, so its test projects always run in one process. Test
shards split them between several modules instead, which a distributed run can hand to different workers. Set the number of shards
in the build host:

```csharp
#:property RocketTestShards=4
```

or in a props file next to it (`<RocketTestShards>4</RocketTestShards>`). Passing it to one run also works:
`dotnet run build/Build.cs -p:RocketTestShards=4`. Unset, `0` or `1` turns shards off. Any other value that is not a whole number
up to 64 is reported as warning `RSMP0001` and turns them off too.

A source generator in the package then writes `TestShard1` to `TestShardN` into the build host. With shards:

- `TestShardPlan` runs once, on the coordinator, after the restore. It finds the test projects the way `TestSolution` does
  (`IncludeTests`, `ExcludeTests` and `TestFramework` apply) and splits them between the shards.
- Each shard runs its projects exactly as `TestSolution` does in `PerProject` mode: a sub module per project, `TestParallelism` at a
  time, with each project's TRX and coverage in its own folder under the tests directory, failing at the end with every project that
  failed.
- `TestSolution` skips itself, so no test runs twice.
- `GenerateCodeCoverageReports` waits for every shard and reads all of their coverage.

The shards are added by the `TestShardsConvention`. A build host that does not import conventions calls
`builder.AddRocketTestShards()`.

### How projects are assigned

Projects are placed longest first, each on the shard with the least work so far, using the last duration of each project's sub
module from `.modularpipelines/estimates` (recorded under `TestSolution` or a shard). A project with no duration counts as the median of
the others. With no durations at all, projects are dealt round-robin in order of their path. The split depends only on its input, so
it does not change between runs unless the durations or the projects do.

The split is worked out **once**, by `TestShardPlan` on the coordinator, and reaches the shards as that module's result, which a
distributed run sends to every worker. Workers never compute it themselves. Their estimates folders can differ from the coordinator's
(each machine restores its own cache, and a worker records the durations of what it ran), and two different splits could run a
project twice or not at all. The coordinator's durations only steer the balance, never which projects run.

The durations of a shard that ran on a worker are saved on that worker. Cache `.modularpipelines` on every instance, or the
coordinator keeps balancing by older durations (or by path).

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

## Distributed runs

The modules that read or write the whole run (GitVersion, restore, build, pack, docs, readme, public API shipping, dependency cleanup
and the coverage report) require the `rocket-coordinator` capability. Every process that is not a distributed worker advertises it,
so a standalone run behaves as before. `TestSolution` and the test shards do not require it: on a worker they restore and build what
they test, and their results travel back to the coordinator as an artifact (`rocket-test-results`, or `rocket-test-shard-results` for
each shard), which the coverage report restores into the tests directory. `TestShardPlan` does require it, so the split of the tests
is always made on the coordinator.

## Docs

`DocsModule` passes `GH_API_TOKEN` (or else `GITHUB_TOKEN`, or `GH_TOKEN`) to the docs build as `GH_API_TOKEN`. A failed docs build is
reported without failing the run; set `Docs:IgnoreFailures: false` to fail it.
