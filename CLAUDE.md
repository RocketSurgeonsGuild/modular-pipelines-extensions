# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Indago is

Indago is a compile-time assembly/type-scanning library for .NET from Rocket Surgeons Guild. It replaces runtime reflection-based DI scanning (the Scrutor / `Rocket.Surgery.DependencyInjection.Analyzers` style) with a Roslyn **incremental source generator** that resolves the scan at build time and emits a strongly-typed `IIndagoProvider`. This makes scanning AOT/trimming-friendly and removes runtime reflection cost.

The public surface (`src/Indago`) is intentionally small; nearly all the work lives in the generator (`src/Indago.Analyzers`).

### How the pieces fit together

- **`src/Indago`** — the runtime-facing API and abstractions (netstandard targets `net8.0;net10.0`).
    - `IIndagoProvider` (`IIndagoProvider.cs`) is the entry point. `GetAssemblies`, `GetTypes`, and `Scan` each take a selector _and_ hidden `[CallerLineNumber]` / `[CallerFilePath]` / `[CallerArgumentExpression]` parameters. The generator keys generated code off the **hash of the selector expression** at a given call site (`GetArgumentExpressionHash`), so changing the selector lambda changes what is generated.
    - `IndagoProviderAttribute` (assembly-level) wires the entry assembly to its generated provider plus a `GeneratedHash` for cache busting. `IIndagoProvider.EntryAssembly` resolves the provider via this attribute.
    - `ServiceRegistrationAttribute` (+ generic `<T>`…`<T1,T2,T3,T4>` variants) and `RegistrationLifetimeAttribute` mark classes for registration; `AddIndagoServiceRegistrations` (`IndagoProviderServiceCollectionExtensions.cs`) scans for them. Default lifetime is **Singleton**.
    - `Abstractions/` holds the fluent selector interfaces (`IReflectionAssemblySelector`, `IServiceDescriptorAssemblySelector`, `ITypeFilter`, etc.) and opt-out attributes (`ExcludeFromIndagoAttribute`).
- **`src/Indago.Analyzers`** — the generator (`netstandard2.0`, `IsRoslynComponent`). `IndagoProviderGenerator` (in `CompiledTypeProviderGenerator.cs`) is the `[Generator]`. It builds three syntax providers — `AssemblyCollection`, `ReflectionCollection`, `ServiceDescriptorCollection` — corresponding to the three `IIndagoProvider` methods.
    - **Cross-assembly cache:** the generator reads/writes `IndagoProvider.ctpjson` (constant `Constants.IndagoProviderCacheFileName`) passed in as an `AdditionalText`. This lets a downstream assembly reuse scan results from a referenced assembly without re-resolving. JSON (de)serialization uses the source-generated `JsonSourceGenerationContext`. `Configuration/` contains all the serializable data records; `AssemblyProviders/` contains the symbol visitors and compiled filters that evaluate selectors against the `Compilation`.
- **`test/TestAssembly`** — a fixture assembly of sample services/types the generator scans in tests.

When changing behavior, the change is usually in `Indago.Analyzers` (what gets generated), and the runtime `Indago` API stays stable.

## Build, test, lint

Two build systems coexist — know which one you're invoking:

- **Nuke** (`.build/Build.cs`, `Pipeline` class) — the canonical CI pipeline. Run via `./build.sh <targets>` (bootstraps the SDK from `global.json` if needed). Default target chain: Restore → Build → Test → Pack. Tests run through Microsoft.Testing.Platform with coverage (cobertura) + trx.
- **ModularPipelines** (`build/Build.cs`, file-based `dotnet run` app with `#:package` directives) — run via `mise run build` (defined as `dotnet run build/Build.cs`).

Day-to-day with the SDK directly:

```bash
dotnet build Indago.sln
dotnet test test/Indago.Tests/Indago.Tests.csproj          # all tests
```

### Tests use TUnit on Microsoft.Testing.Platform

`global.json` sets `"test": { "runner": "Microsoft.Testing.Platform" }` and the test project is `OutputType=Exe`. Tests use **TUnit** (`[Test]`, `[MethodDataSource]`, `[DependsOn]`, `[Timeout]`), **Shouldly** assertions, **FakeItEasy** mocks, and **Verify** for snapshots. The generator helper is `Rocket.Surgery.Extensions.Testing.SourceGenerators` (`GeneratorTest` base class, fluent `Builder`).

Because tests run as an MTP executable, filter a single test by running the project and passing MTP args, e.g.:

```bash
dotnet run --project test/Indago.Tests/Indago.Tests.csproj -- --treenode-filter "/*/*/AssemblyScanningTests/Should_Generate_All_The_Things"
```

**Snapshot tests:** generated output is verified against `test/Indago.Tests/snapshots/*.verified.cs`. A failing snapshot test means the generated code changed. Review the `*.received.*` diff; if the change is intended, accept it (e.g. `dotnet verify accept` / move received → verified, or use your Verify diff tool). Temp paths are scrubbed to `{TempPath}` in snapshots.

### Linting / formatting

`hk` (managed via mise) runs git hooks; formatting is **prettier** (with XML/YAML/TOML/PowerShell plugins) plus `dotnet format` / JetBrains cleanup. `mise` postinstall runs `hk install --mise`, `apm install`, `skillfile install`, and `dotnet restore`.

## Conventions that matter here

- **Central Package Management:** all versions live in `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`). Add a `<PackageVersion>` there, reference without a version in the csproj. `GlobalPackageReference` entries apply analyzers/build tooling to every project.
- **Strict analysis:** `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=preview`, `AnalysisMode=AllEnabledByDefault`, `Features=strict`. Roslynator + BannedApiAnalyzers + NETAnalyzers are on. `RS0017` is treated as an error (public API tracking). NuGet audit is enabled (`all`, moderate level).
- **Global usings** (from `Directory.Build.props`): `JetBrains.Annotations`, `System.Diagnostics.CodeAnalysis`, and `NotNullAttribute` alias. Public types are annotated `[PublicAPI]`.
- **Analyzer project constraints:** `Indago.Analyzers` is `netstandard2.0` with `EnforceExtendedAnalyzerRules`. It uses `Polyfill` for newer language/runtime APIs. Don't reference runtime-only packages from it.
- **Two `build`-named directories:** `.build/` (Nuke) and `build/` (ModularPipelines) are different build systems; `src/Indago/build*/` directories are NuGet MSBuild `build`/`buildTransitive`/`buildMultiTargeting` props/targets packed into the package — not build scripts.

## Repo layout quick reference

- `src/Indago` — runtime API + abstractions (the NuGet package `Indago`)
- `src/Indago.Analyzers` — the incremental source generator (packed into `Indago` as an analyzer)
- `test/Indago.Tests` — generator + snapshot tests (TUnit/Verify)
- `test/TestAssembly` — sample types scanned by tests
- `.build/` — Nuke pipeline · `build/` — ModularPipelines pipeline
