# AGENTS.md

Daily contract for changing the **clean-backend-template** itself (not a generated product — generated products get their own AGENTS.md). Read this before writing code.

## What this repo is

A .NET 10 clean-backend baseline whose canonical route is now a standard static `dotnet new clean-backend` template under `template/`. Repository development projects remain outside `template/`:

- `src/Product.Guardrails.Analyzers` — Roslyn analyzers `PGB001`, `PGB003`, `PGB006`. The external package ID is `CleanBackend.Guardrails.Analyzers`.
- `src/Product.Abstractions` — development copy of shared `Result`, `Error`, synchronous `IValidator` conventions.
- `src/Product.Template.Tool` — legacy/optional `bootstrap`, `new-feature`, and `verify` CLI. It is no longer the architectural center.
- `template/` — generated-product skeleton only: API, copied runtime abstractions, tests, product-local verifier, docs, policy, and local analyzer feed.

## Hard invariants

1. **Generated products must be portable.** They reference the analyzer via pinned `PackageReference` against a product-local feed (`eng/local-feed`), never an absolute path into this repo's `bin`.
2. **The static template is isolated.** Do not place `.template.config/template.json` at the repository root.
3. **The analyzer package and lock file are a matched pair.** Bump package versions when content changes and refresh the template `.nupkg` plus `packages.lock.json` together.
4. **PGB003 is heuristic and non-breaking by default.** Do not promote it to warning/error in the canonical template without real-product evidence.
5. **`new-feature` must not silently overwrite.** Check all intended files before writing.
6. **Verification remains a real product-local gate.** Keep required files, analyzer package reference, module facade/public-type checks, package policy, lock file checks, unresolved-placeholder checks, and guardrail-exception validation/expiry.

## How to verify

Use the workspace-local SDK if present, otherwise system `dotnet` matching `global.json`:

```bash
./.dotnet/dotnet build src/Product.Abstractions/Product.Abstractions.csproj --nologo
./.dotnet/dotnet build src/Product.Guardrails.Analyzers/Product.Guardrails.Analyzers.csproj --nologo
./.dotnet/dotnet build src/Product.Template.Tool/Product.Template.Tool.csproj --nologo
./.dotnet/dotnet run --project tests/Product.Guardrails.Analyzers.Tests/Product.Guardrails.Analyzers.Tests.csproj
./.dotnet/dotnet run --project tests/Product.Template.Tool.Tests/Product.Template.Tool.Tests.csproj
```

CI must also prove the static `dotnet new` path: install `template/`, create a non-`Product` project, clear analyzer caches, locked-restore immediately against the committed local-feed package, build, test, run product-local verification, search for this repository's absolute path, and ensure no `bin/`/`obj/` directories were copied.

## Do not add yet

No database provider, migrations, tenancy/auth implementation, background jobs, caching, broker/outbox, OpenTelemetry exporters, API-versioning tooling, shared runtime package, external package registry/feed, release automation, Dependabot/Renovate, or new analyzer rules without real-product evidence.
