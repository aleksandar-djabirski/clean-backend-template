# Architecture

The repository now centers on a working static template plus real reference code, `AGENTS.md`, analyzers, and product-local verification. Standard `dotnet new clean-backend` is the canonical creation path. The legacy CLI generator is optional convenience/backward-compatible tooling.

The template is isolated in `template/` so `sourceName` replacement never touches repository-development projects such as `src/Product.Guardrails.Analyzers` or `src/Product.Template.Tool`.

Generated products use a single API assembly with module folders. This is a modular monolith trade-off; it is not true module isolation. Escalate to separate projects only when a real product needs compiler-enforced boundaries.

Analyzers are dev-time tooling. Runtime abstractions are copied source owned by each product. Analyzer publishing/versioned upgrades and any shared feed are deferred until Product #2.
