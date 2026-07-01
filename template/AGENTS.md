# AGENTS.md

Daily contract for changing this generated product.

- Learn primarily from this file and the real reference code in `src/Product.Api` and `src/Product.Abstractions`.
- This is a rename-only static template; do not assume hidden generator options selected a database, auth provider, tenancy model, broker, cache, or telemetry vendor.
- Modules are a single-assembly modular monolith unless the product deliberately escalates a module into its own project.
- PGB001 and PGB006 are build-breaking structural guardrails. PGB003 is a heuristic awaiting real-product evidence and must remain visible but non-breaking by default.
- Analyzers do not prove business correctness, authorization correctness, absence of indirect writes, true module isolation, or absence of duplicate business logic.
- Runtime abstractions are copied source owned by this product. Do not centralize them into shared packages until multiple real products prove stability.
- Verification is product-local: run `dotnet run --project eng/Product.Verify/Product.Verify.csproj -- --product .`.
- The local analyzer feed package and `packages.lock.json` are a matched pair; refresh them together and bump package versions when package content changes.
