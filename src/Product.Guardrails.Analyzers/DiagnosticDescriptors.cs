using Microsoft.CodeAnalysis;

namespace Product.Guardrails.Analyzers;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor QueryEfMutation = new(
        "PGB001",
        "Query handlers must not mutate EF Core persistence state",
        "Query handler '{0}' performs EF Core persistence mutation through '{1}'",
        "ProductGuardrails",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Move persistence mutations to a command handler. PGB001 is intentionally limited to symbol-confirmed EF Core mutation APIs.");

    public static readonly DiagnosticDescriptor EndpointAdapterShape = new(
        "PGB006",
        "Endpoint mappings must stay inside endpoint adapters and avoid forbidden dependencies",
        "{0}",
        "ProductGuardrails",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Endpoint adapters must be explicitly marked and may receive only bound request data, CancellationToken, and one typed command/query handler.");

    public static readonly DiagnosticDescriptor GenericPersistenceWrapper = new(
        "PGB003",
        "Generic persistence wrappers are not part of the Phase 0 baseline",
        "Type '{0}' looks like a generic repository or CRUD persistence wrapper",
        "ProductGuardrails",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Use explicit business capabilities instead of broad generic persistence wrappers. This Phase 0 rule is deliberately limited.");
}
