# AGENTS.md

Daily contract for changing the **clean-backend-template** itself (not a generated product —
generated products get their own AGENTS.md). Read this before writing code.

## What this repo is

A generator + guardrails for .NET 10 SaaS backends. Three source projects:

- `src/Product.Guardrails.Analyzers` — Roslyn analyzers `PGB001`, `PGB003`, `PGB006`. Packed as a
  NuGet package so generated products reference it portably.
- `src/Product.Abstractions` — shared `Result`, `Error`, synchronous `IValidator`.
- `src/Product.Template.Tool` — the `bootstrap` / `new-feature` / `verify` CLI that scaffolds and
  checks generated products.

Tests live in `tests/` and are plain console programs (no framework), run with `dotnet run`.

## Hard invariants — do not regress

1. **Generated products must be portable.** They reference the analyzer via `PackageReference`
   against a product-local feed (`eng/local-feed`), never an absolute path into this repo's `bin`.
   If you touch packaging, re-prove a generated product builds after being moved off this machine.
2. **`--kind` is authoritative**, not the feature name. Command/query registration is derived from
   the generated handler file, never a name heuristic.
3. **Analyzers ship with fixtures for both the violation and the valid boundary case.** No analyzer
   change merges without a passing/failing fixture pair.
4. **`verify` is a real gate**: structure checks + analyzer-enabled build + locked restore +
   package policy + guardrail-exception validity. Keep it that way.

## Admission control (the point of the project)

Do not add analyzers, controls, or dependencies on impulse. A new guardrail belongs in the
baseline only when it prevents a serious or recurring failure, is deterministic enough to avoid
routine false positives, has fixtures for violation and boundary cases, and is exercised by the
generated-product smoke tests. See `README.md` "What Not To Add Yet" and the Phase 3 backlog.

## How to verify (workspace-local SDK)

```bash
./.dotnet/dotnet build src/Product.Abstractions/Product.Abstractions.csproj --nologo
./.dotnet/dotnet build src/Product.Guardrails.Analyzers/Product.Guardrails.Analyzers.csproj --nologo
./.dotnet/dotnet build src/Product.Template.Tool/Product.Template.Tool.csproj --nologo
./.dotnet/dotnet run --project tests/Product.Guardrails.Analyzers.Tests/Product.Guardrails.Analyzers.Tests.csproj
./.dotnet/dotnet run --project tests/Product.Template.Tool.Tests/Product.Template.Tool.Tests.csproj
```

The two test runs are the evidence. Do not claim a change works without them green.

## Before you say it works

Run the analyzer fixtures and the generated-product smoke tests. The smoke tests bootstrap a
throwaway product, scaffold command/query/weather features, and build under the real analyzers.
Verification output is the evidence — paste it, don't assert it.
