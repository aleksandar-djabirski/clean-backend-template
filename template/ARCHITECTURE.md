# Architecture

This product starts as a single-assembly modular monolith. Modules are folders under `src/Product.Api/Modules`. Escalate a module to its own project only after a real product need proves the extra cost is worth it.

Runtime abstractions (`Result`, `Error`, `IValidator`, endpoint mapping) are copied source owned by this product, not shared runtime packages. Analyzers are dev-time tooling; runtime contracts are behavioral and should be extracted only after multiple real products prove stability.

Analyzers enforce a limited structural set: PGB001 and PGB006 are build-breaking; PGB003 is heuristic and visible but not build-breaking by default. They do not prove business correctness, authorization correctness, absence of indirect writes, true module isolation, or absence of duplicate business logic.
