# Clean Backend Template

This repository is a reusable backend template for SaaS products built on .NET 10.

The goal is not to automate every architecture or business decision. The goal is to automate structural mistakes that tools can reliably catch, then make semantic risks visible through ownership evidence, focused tests, review, and narrow human decisions only where judgment is unavoidable.

The template is considered useful only when a fresh generated product can prove the real path:

```text
bootstrap product
-> scaffold a command and query
-> build with analyzers enabled
-> run analyzer fixtures
-> run generated-product verification
-> pass CI
```

## Current Status

Phase 0 is implemented as the feasibility gate:

- `Product.Guardrails.Analyzers` implements `PGB001`, `PGB006`, and a limited `PGB003` spike.
- `Product.Template.Tool` implements `bootstrap`, `new-feature`, and `verify`.
- Analyzer fixtures prove passing and failing cases.
- Generated-product smoke tests create a disposable `SampleProduct`, scaffold `CreateRequest` and `GetRequest`, build, and verify it.
- CI runs the same practical path instead of checking only that files exist.

The minimal Phase 1 baseline is also scaffolded:

- `Product.Abstractions` provides shared `Result`, `Error`, and validation conventions.
- Generated products include an `{Product}.Abstractions` project and an API project reference to it.
- Generated products include a module facade, ownership map, module capability map, guardrail exception skeleton, package policy skeleton, migration safety profile, and vendor-neutral observability baseline.
- `verify` checks required Phase 1 files, analyzer reference, abstractions reference, module facades, unresolved placeholders, and internal-by-default module implementation types.
- The smoke tests have two named generated-product paths: the baseline command/query smoke path, and a Phase 3 candidate `weather-query` smoke fixture. Weather is a mechanical reference fixture similar in spirit to a framework weather sample; it is not product business logic and does not prove Phase 2 product usefulness or Phase 3 readiness.

Target SDK and framework are pinned to .NET `10.0.301` and `net10.0`.

The repo uses `.slnx` because .NET 10 makes it the default solution format. It is smaller, XML-based, easier to diff, and avoids legacy `.sln` noise. If a downstream tool cannot handle `.slnx`, that compatibility problem should be explicit rather than silently carrying two solution files.

## Why This Shape

The template is for repeated SaaS/client/product work, not a one-off prototype. The permanent baseline must stay economically sustainable for a solo developer.

A guardrail belongs in the baseline only when it prevents a serious or recurring failure, is deterministic enough to avoid routine false positives, has tests for violations and valid boundary cases, and proves itself in generated-product smoke tests and real reference work.

That is why Phase 0 is deliberately small. It proves the machinery before adding more policy:

- `PGB001` blocks direct EF Core mutations from query handlers without pretending to detect every indirect write.
- `PGB006` keeps route mappings in explicit endpoint adapters and blocks dangerous direct endpoint dependencies.
- `PGB003` is only a feasibility spike for obvious generic repository/CRUD wrappers. It must not be treated as semantic proof.
- The generated sample module is disposable verification material, not product business logic.

## What Not To Add Yet

Do not add more analyzers, OpenAPI compatibility tooling, query-count helpers, migration compatibility fixtures, outbox/broker/caching integrations, distributed tracing exporters, or special HTTP endpoint categories until a real product need or feasibility spike justifies them.

OpenTelemetry is useful, but the baseline starts with vendor-neutral .NET observability conventions: `Activity`, W3C trace context, correlation IDs, structured request-completion logging, health endpoints, and safe error trace identifiers. Do not add OpenTelemetry packages/exporters until a product chooses an observability destination or a later feasibility spike proves the SDK integration belongs in the reusable baseline.

## Verify

Use the workspace-local SDK:

```bash
./.dotnet/dotnet build src/Product.Guardrails.Analyzers/Product.Guardrails.Analyzers.csproj --nologo
./.dotnet/dotnet build src/Product.Abstractions/Product.Abstractions.csproj --nologo
./.dotnet/dotnet build src/Product.Template.Tool/Product.Template.Tool.csproj --nologo
./.dotnet/dotnet run --project tests/Product.Guardrails.Analyzers.Tests/Product.Guardrails.Analyzers.Tests.csproj
./.dotnet/dotnet run --project tests/Product.Template.Tool.Tests/Product.Template.Tool.Tests.csproj
```

If using a machine-level SDK, the same commands work with `dotnet` when SDK `10.0.301` is installed.

## Phase Direction

Phase 1 has added only the core baseline pieces that are cheap and mechanically verifiable:

- `Product.Abstractions`
- module facade conventions
- ownership and module capability maps
- Result and Error conventions
- validation wrapper
- package policy
- architecture verification
- guardrail exception mechanism
- vendor-neutral observability baseline skeleton
- migration safety policy skeleton

Do not continue straight into every later control. The in-memory Weather endpoint is useful as a generated reference feature, but it is not enough to enter Phase 3. Phase 2 is first real product validation. Phase 3 is admission-controlled expansion. Phase 4 is periodic sustainability review.

## Phase 3 Candidate Smoke Fixture

The generated `weather-query` feature is the current Phase 3 candidate smoke fixture. It proves that the tool can create and verify a small read-only in-memory endpoint shape beyond the baseline command/query path.

It proves mechanics only:

- `new-feature --kind weather-query` generates endpoint, query, handler, and response records.
- The generated endpoint stays inside an explicit endpoint adapter.
- The generated product still builds with guardrail analyzers enabled.
- `verify` still passes on the generated output.

It does not prove Phase 3 admission:

- It has no persistence, migration, provider, external API, background workflow, caching, OpenTelemetry exporter, special HTTP category, ownership ambiguity, or production performance pressure.
- It must not be used as evidence that Phase 3 controls belong in the permanent baseline.
- Phase 3 still requires real product pressure or repeated reference work before adding durable controls.

## Phase 3 Backlog

Phase 3 items are not assumed to belong in every SaaS product. They stay out of the default baseline until a real product or repeated reference work proves the control prevents more cost than it creates.

Candidate controls:

- Persistence and schema evolution profile: choose EF migrations, SQL migrations, an external migrator, managed-service schema setup, or no database. Every persistent product should make this decision explicitly, but the template should not force EF migrations as the only approach.
- Migration compatibility fixtures: add when a product has persistent data and needs rolling-compatible deployments, destructive transformations, or prior-schema upgrade tests.
- Query-count helpers: add when real collection/dashboard endpoints risk N+1 queries or excessive database round trips.
- OpenAPI/API compatibility tooling: add when external clients depend on stable HTTP contracts.
- Specialized HTTP endpoint categories: add when real products need uploads, downloads, streaming responses, ETags, cacheable representations, or server-sent events.
- Provider-specific guardrails: add after selecting real providers such as PostgreSQL, SQL Server, Redis, object storage, auth providers, payment providers, or email providers.
- Outbox, broker, and background-job conventions: add when a real workflow needs reliable asynchronous side effects.
- Caching conventions: add only after measured or expected performance pressure justifies cache invalidation complexity.
- OpenTelemetry packages/exporters: add when a product chooses a telemetry backend or needs cross-process trace collection beyond built-in .NET primitives.
- API compatibility, package compatibility, or binary compatibility checks: add only when the project has consumers that make compatibility a real risk.
- Promotion or retirement of `PGB003`: decide after real reference code shows whether generic repository/CRUD-wrapper detection has high signal and low false positives.

Admission criteria:

- A real product need or repeated failure exists.
- A feasibility spike proves the control is deterministic enough.
- Tests prove both violation and valid boundary cases.
- Generated-product smoke tests and CI exercise the control.
- Documentation and scaffolding are updated together.
- The maintenance cost is lower than the failure cost it prevents.
