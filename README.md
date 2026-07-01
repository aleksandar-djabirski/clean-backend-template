# Clean Backend Template

This repository is a reusable .NET 10 SaaS backend baseline. The canonical start path is now a real, runnable, rename-only static template:

```bash
dotnet new install ./template
dotnet new clean-backend --name My.Product --output ../My.Product
cd ../My.Product
dotnet restore src/My.Product.Api/My.Product.Api.csproj --locked-mode
dotnet run --project eng/My.Product.Verify/My.Product.Verify.csproj -- --product .
```

The custom `Product.Template.Tool bootstrap` command remains only for backward-compatible smoke coverage and convenience. It is not required before a product can begin. `new-feature` remains optional scaffolding and fails rather than silently overwriting existing feature files.

## What the Template Proves

The static template under `template/` is isolated from this repository's development projects. It contains generated-product source only: API, copied runtime abstractions, tests, product-local verification, local analyzer feed, package policy, guardrail exception skeleton, ownership/module capability documentation, and generated-product documentation.

The reference Weather endpoint is intentionally mechanical. It exercises:

- copied `Result`, `Error`, `IValidator`-style runtime conventions;
- explicit endpoint adapters;
- endpoint result/error mapping;
- generated-product tests;
- analyzer-enabled builds and product-local verification.

It does **not** prove persistence, migrations, tenancy, authorization, integrations, true business correctness, production readiness, or absence of indirect writes.

## Guardrails

`Product.Guardrails.Analyzers` builds the analyzer source, but the external NuGet package identity is stable and neutral: `CleanBackend.Guardrails.Analyzers`. Generated products reference it by pinned `PackageReference` with `PrivateAssets="all"` from `eng/local-feed`.

- `PGB001` blocks direct EF Core mutation APIs in query handlers.
- `PGB006` keeps route mappings in endpoint adapters and blocks forbidden direct endpoint dependencies.
- `PGB003` is a heuristic repository/CRUD-wrapper detector. It remains visible at analyzer-default Info severity and is not build-breaking by default, including under `TreatWarningsAsErrors`.

Analyzers enforce only this limited structural set. They do not prove business correctness, authorization correctness, absence of indirect writes, true module isolation, or absence of duplicate business logic.

## Runtime Abstractions

Runtime abstractions are deliberately copied into each generated product as source: `Result`, `Result<T>`, `Error`, `ErrorType`, `IValidator`, and endpoint-result mapping. They are behavioral contracts, not shared runtime packages. Centralizing them now would risk creating a premature mini-framework. Copy first; extract later only after multiple real products prove stability.

## Portability and Package Maintenance

Generated products use a product-local feed (`eng/local-feed`) plus `nuget.org` in `nuget.config`. There are no absolute paths back to this repository.

The committed analyzer `.nupkg` and `src/Product.Api/packages.lock.json` in `template/` are a matched pair. Locked restore validates package content hashes. When analyzer package content changes:

1. bump the analyzer package version;
2. pack the analyzer;
3. replace the template local-feed package;
4. regenerate the template lock file against that exact package;
5. commit both together.

A package ID/version is immutable. Do not replace same-version package content in a generated product. A shared analyzer feed and deliberate per-product analyzer upgrade workflow are deferred until Product #2 exists.

## Architecture

Generated products are single-assembly modular monoliths. Modules are folders under the API project unless a product deliberately escalates a module into its own project. Verification and analyzers are backstops, not true compiler-enforced isolation.

Verification remains product-local (`eng/<Product>.Verify`) until its layout assumptions stabilize. It checks required files, analyzer package reference, module facade/public-type conventions, package policy, lock file presence, unresolved placeholders, and guardrail exception validity/expiry.

## Verify This Repository

Use the workspace-local SDK if present, otherwise a system `dotnet` SDK pinned to `10.0.301` by `global.json`:

```bash
./.dotnet/dotnet build src/Product.Abstractions/Product.Abstractions.csproj --nologo
./.dotnet/dotnet build src/Product.Guardrails.Analyzers/Product.Guardrails.Analyzers.csproj --nologo
./.dotnet/dotnet build src/Product.Template.Tool/Product.Template.Tool.csproj --nologo
./.dotnet/dotnet run --project tests/Product.Guardrails.Analyzers.Tests/Product.Guardrails.Analyzers.Tests.csproj
./.dotnet/dotnet run --project tests/Product.Template.Tool.Tests/Product.Template.Tool.Tests.csproj
```

CI also installs `template/`, creates a non-`Product` generated product, clears the NuGet cache for the analyzer, restores locked mode immediately, builds, runs generated tests, runs product-local verification, searches for this repository's absolute path, and checks that no `bin/` or `obj/` directories were copied.

## Deferred Until the First Real Product

The next meaningful validation is one real database-backed vertical slice in Product #1 after that product makes real provider, auth, and tenancy decisions. Do not add EF Core, database providers, migrations, authentication providers, tenancy implementation, brokers, outbox, background jobs, caches, OpenTelemetry exporters, API-versioning tooling, or new analyzer rules to the generic template without real evidence.
