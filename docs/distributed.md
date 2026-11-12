# Distributed builds

`Rocket.Surgery.ModularPipelines.Extensions.Distributed` runs one pipeline across several CI runners with the distributed mode of
ModularPipelines v4. One process is the **master** (the coordinator): it plans the run, hands out modules and runs modules itself.
The other processes are **workers**: they claim the modules the master offers them and send the results back. The processes meet
through Redis.

Distributed mode is opt-in and turns itself off when it has nothing to work with. Without a Redis connection the pipeline runs
exactly as it does without the package, so fork and Dependabot pull requests, which get no secrets, still build.

> [!WARNING]
> Distributed mode builds on pre-release packages: ModularPipelines `4.0.0-rc.1` and its Redis integration, which is labelled
> beta and depends on the pre-1.0 Respire client. Read [Risks](#risks) before you turn it on.

## Requirements

- **Redis 7.4 or later, or Valkey 9 or later.** The coordinator uses `HPEXPIRE` and `SPUBLISH`, which older servers do not have.
  Set `maxmemory-policy noeviction`: an evicted lease or result breaks the run.
- **Every runner can reach Redis.** GitHub-hosted runners need a public endpoint, so use a managed service (Upstash, Redis Cloud
  and so on) or a server you expose with TLS.
- **A command budget.** Each process sends a heartbeat every 5 seconds, and the master renews leases and polls for results. A few
  runs a day can exhaust a free tier that counts commands (Upstash's free tier allows about 10,000 a day). Budget for a paid plan or
  a server you run yourself.
- **The connection string as a secret**, `REDIS_URL`, in the Respire format: `host:port`, a `redis://` or `rediss://` URI, or a
  StackExchange.Redis-style string such as `host:6380,password=…,ssl=true`. Respire rejects options it does not know, such as
  `abortConnect`. If the password contains a comma, set the password in code with `ConfigureRedis` (see below).
- **Secrets are not available to forks or Dependabot.** Those runs fall back to one standalone runner, so they do not get faster.
- **The same build host binary on every runner** (see [Publish the build host once](#publish-the-build-host-once)).

## Turning it on

Reference the package from the build host, next to the core package:

```csharp
#:package Rocket.Surgery.ModularPipelines.Extensions.Distributed@<version>
```

The package exports a convention, so a build host that imports conventions (`#:property ImportConventions=true` and
`ConventionsPlugin`) picks it up without code changes. The convention calls `AddRocketDistributed` with the settings read from the
environment and configuration.

Without the conventions plugin, call it yourself:

```csharp
var builder = Pipeline.CreateBuilder(args);
builder.AddRocketDistributed(options => options.MaxParallelism = 2);
```

With the conventions plugin, call `AddRocketDistributed` **before** `AddPlugin(new ConventionsPlugin(...))`: the convention then
sees that it ran and steps aside. Calling it twice throws.

Distributed mode is used only when **all** of these hold:

1. `Pipeline:Distributed:Enabled` is not `false`.
2. A Redis connection string is set (`REDIS_URL`, or `Pipeline:Distributed:Redis`).
3. `MODULARPIPELINES_TOTAL_INSTANCES` is greater than one.

Otherwise nothing is registered and the run is the normal standalone run. A developer with `REDIS_URL` set for something else
therefore still builds locally, because the instance count defaults to one.

### Settings

Environment variables win over the `Pipeline:Distributed` configuration section.

| Setting                    | Environment variable               | Configuration key                     | Default                                                        |
| -------------------------- | ---------------------------------- | ------------------------------------- | -------------------------------------------------------------- |
| Enabled                    |                                    | `Pipeline:Distributed:Enabled`        | `true`                                                         |
| Redis connection string    | `REDIS_URL`                        | `Pipeline:Distributed:Redis`          | none, so distributed mode is off                               |
| Instance index             | `MODULARPIPELINES_INSTANCE_INDEX`  | `Pipeline:Distributed:InstanceIndex`  | `0`                                                            |
| Instance count             | `MODULARPIPELINES_TOTAL_INSTANCES` | `Pipeline:Distributed:TotalInstances` | `1`                                                            |
| Role                       | `MODULARPIPELINES_ROLE`            | `Pipeline:Distributed:Role`           | `Auto`                                                         |
| Run id                     | `MODULARPIPELINES_RUN_ID`          | `Pipeline:Distributed:RunId`          | `{GITHUB_RUN_ID}-{GITHUB_RUN_ATTEMPT}` on GitHub Actions       |
| Modules at once per runner | `MODULARPIPELINES_MAX_PARALLELISM` | `Pipeline:Distributed:MaxParallelism` | the pipeline's own limit                                       |
| Redis key prefix           |                                    | `Pipeline:Distributed:KeyPrefix`      | `rocket-{owner}-{repository}` on GitHub Actions, else `rocket` |

The timeouts are set in code on `RocketDistributedOptions`. Their defaults allow for GitHub runners, which start minutes apart and
stall now and then:

| Option                      | Default     | ModularPipelines default | Why                                                              |
| --------------------------- | ----------- | ------------------------ | ---------------------------------------------------------------- |
| `WorkerRegistrationTimeout` | 10 minutes  | 5 minutes                | A worker's runner may still be installing the SDK.               |
| `MasterStartTimeout`        | 15 minutes  | 5 minutes                | The master's runner may start late, or restore a large cache.    |
| `MasterTimeout`             | 2 minutes   | 1 minute                 | Tolerates a busy master before a worker gives up.                |
| `WorkerTimeout`             | 60 seconds  | 30 seconds               | Tolerates a stalled worker before its module is requeued.        |
| `WorkerHeartbeatInterval`   | 5 seconds   | 5 seconds                |                                                                  |
| `ModuleResultTimeout`       | 45 minutes  | 45 minutes               |                                                                  |
| `TimeToLive` (Redis keys)   | 2 hours     | 1 hour                   | Must exceed `ModuleResultTimeout`.                               |
| `RunCleanup`                | `OnSuccess` | `Always`                 | Keeps a failed run's Redis data until it expires, for diagnosis. |

`ConfigureDistributed` and `ConfigureRedis` give the last word on the ModularPipelines `DistributedOptions` and `RedisOptions`, for
example to set TLS options or a password through `RedisOptions.ConfigureConnection`.

Artifacts travel through Redis by default. For large artifacts set `UseRedisArtifactStore = false` and register another store
yourself, for example `AddS3DistributedArtifactStore` from `ModularPipelines.Distributed.Artifacts.S3` (Cloudflare R2, MinIO and
other S3-compatible stores work).

## Roles

| Process                                        | Role   | Coordinator capability |
| ---------------------------------------------- | ------ | ---------------------- |
| Instance 0 (or `MODULARPIPELINES_ROLE=Master`) | master | yes                    |
| Instance 1 and up (or `Worker`)                | worker | no                     |
| Standalone run (no Redis or one instance)      | none   | yes                    |

ModularPipelines places modules with **capabilities**. Modules that must see the whole run declare
`[RequiresCapability(RocketCapabilities.Coordinator)]`, and only the master advertises
`RocketCapabilities.Coordinator` (`rocket-coordinator`). The core package advertises it on every process that is not a distributed
worker, which covers standalone runs; this package also adds it to the master's `DistributedOptions.Capabilities`. Add your own with
`options.Capabilities.Add(...)`. Each runner also advertises its operating system (`linux`, `windows`, `macos`) automatically.

## What runs where

The core modules are placed like this:

| Module                                                                                                                      | Runs on                       |
| --------------------------------------------------------------------------------------------------------------------------- | ----------------------------- |
| GitVersion, Restore, Build, package icon, Pack, Docs, Ship public APIs, Readme, Remove unused dependencies, coverage report | the master only               |
| `TestShardPlan` (with test shards)                                                                                          | the master only               |
| `TestSolution`, or the test shards `TestShard1` to `TestShardN`                                                             | any process, master or worker |
| Your own modules without a capability requirement (for example AOT sample publishing)                                       | any process, master or worker |

The unit of distribution is the **module**. `TestSolution` is one module: it runs every test project as a sub module, in parallel,
on whichever process claims it. The test projects are not spread across workers, unless you turn on
[test shards](history-and-tests.md#test-shards): `#:property RocketTestShards=<n>` in the build host splits the tests into `n` modules
that different workers can run. Set it to the number of instances, so each runner can take one shard. Distribution helps by running `TestSolution`, and
your own modules that are not pinned to the coordinator, on a worker at the same time as the master packs and builds the docs. Expect
the gain to be roughly the shorter of those two paths, minus the time a worker spends setting up and building what it tests. A
pipeline whose modules are nearly all pinned to the coordinator gains nothing, so keep the matrix small: two or three instances.

A module that runs on a worker reaches the master only through its result and its declared artifacts. Files it writes are on the
worker's disk, which the workflow does not upload. A module whose files the master needs declares them with `[ProducesArtifact]`, and
the consumer declares `[ConsumesArtifact]`; they travel through the artifact store (Redis by default).

## Publish the build host once

Every process stamps its registration with a `PipelineSchemaVersion` that includes the build ids of the assemblies that define its
modules and results. A process whose stamp differs from the master's is rejected before it runs anything. If every runner compiles
`build/Build.cs` itself, the ids can differ and the run fails with a schema mismatch.

So publish the build host once and run that same binary everywhere:

```shell
dotnet publish build/Build.cs -c Release -o "$RUNNER_TEMP/pipeline-host" -p:PublishAot=false
dotnet "$RUNNER_TEMP/pipeline-host/Build.dll"
```

`-p:PublishAot=false` is needed because a file-based app publishes with Native AOT by default, and the conventions plugin and
distributed result serialization use reflection. Never use `dotnet run build/Build.cs` on a runner of a distributed run.

Upgrade the master, the workers and this library together: the wire format between processes changes between ModularPipelines
versions.

## The workflow

[`samples/distributed-build.yml`](samples/distributed-build.yml) is a complete GitHub Actions workflow:

1. `host` checks whether the `REDIS_URL` secret is available, picks the instance list (`[0, 1]`, or `[0]` without the secret),
   publishes the build host and uploads it.
2. `pipeline` is a matrix over the instance list. Each entry downloads the same host and runs it with
   `MODULARPIPELINES_INSTANCE_INDEX`, `MODULARPIPELINES_TOTAL_INSTANCES`, `MODULARPIPELINES_RUN_ID` and `REDIS_URL` set. Instance
   0 restores and saves `.modularpipelines/run-history` with `actions/cache` (only the master writes run history, and the scheduler
   orders work by the durations it records) and uploads the logs, test results, coverage, run report and packages.
3. `publish` downloads the `nuget` artifact the master uploaded.

Without the secret the matrix has one entry, the instance count is one, and that entry runs the normal standalone build.

## Risks

- **Pre-release packages.** ModularPipelines `4.0.0-rc.1` targets .NET 11 only, its Redis package is beta and Respire is pre-1.0.
  Pin exact versions and expect breaking changes between release candidates.
- **No partial re-runs.** "Re-run failed jobs" restarts only some instances, and they wait for a master that never comes. The
  sample workflow fails such a re-run at once; use "Re-run all jobs".
- **Work is not exactly-once.** A module on a lost worker is requeued once (`MaxModuleLeaseExpirations` is 2). Modules with side
  effects outside the run belong on the master.
- **Linux runners only, with the same workspace path.** Results carry absolute paths (`File`, `Folder`) and the build host has its
  build scripts' root compiled in, so all runners must check the repository out at the same path on the same OS. `ubuntu-latest`
  gives `/home/runner/work/<repo>/<repo>` everywhere; do not mix in Windows or macOS runners.
- **Billed minutes go up.** Each runner repeats checkout, SDK setup, restore and the build of what it runs. Wall-clock time can drop,
  but the minutes billed rise. Compare whole-job times in GitHub, and the per-worker utilization in `artifacts/run-report.json`,
  before adding instances.
- **Redis limits.** Command quotas, memory limits and an evicting memory policy all fail runs. Large artifacts belong in an S3
  store, not Redis.
- **Forks and Dependabot** run standalone and see no speed-up.
