using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Product.Guardrails.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProductGuardrailsAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> EfMutationMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Add",
        "AddAsync",
        "AddRange",
        "AddRangeAsync",
        "Attach",
        "AttachRange",
        "Remove",
        "RemoveRange",
        "SaveChanges",
        "SaveChangesAsync",
        "Update",
        "UpdateRange");

    // Bulk and raw-SQL mutations are exposed as EF Core extension methods whose containing
    // type is RelationalQueryableExtensions / RelationalDatabaseFacadeExtensions, not a
    // DbContext/DbSet. They are recognized by method name within the EF Core namespace.
    private static readonly ImmutableHashSet<string> EfExtensionMutationMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ExecuteDelete",
        "ExecuteDeleteAsync",
        "ExecuteUpdate",
        "ExecuteUpdateAsync",
        "ExecuteSql",
        "ExecuteSqlAsync",
        "ExecuteSqlRaw",
        "ExecuteSqlRawAsync",
        "ExecuteSqlInterpolated",
        "ExecuteSqlInterpolatedAsync");

    private static readonly ImmutableHashSet<string> RouteMappingMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "MapGet",
        "MapPost",
        "MapPut",
        "MapDelete",
        "MapPatch");

    private static readonly ImmutableHashSet<string> CrudMemberNames = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Add",
        "AddAsync",
        "Create",
        "CreateAsync",
        "Delete",
        "DeleteAsync",
        "Find",
        "FindAsync",
        "Get",
        "GetAsync",
        "List",
        "ListAsync",
        "Remove",
        "RemoveAsync",
        "Update",
        "UpdateAsync");

    private static readonly ImmutableHashSet<string> ExplicitForbiddenTypeNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Microsoft.AspNetCore.Http.HttpContext",
        "Microsoft.Extensions.Configuration.IConfiguration",
        "System.IServiceProvider",
        "System.Security.Claims.ClaimsPrincipal",
        "MediatR.IMediator",
        "MediatR.ISender");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            DiagnosticDescriptors.QueryEfMutation,
            DiagnosticDescriptors.EndpointAdapterShape,
            DiagnosticDescriptors.GenericPersistenceWrapper);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (method is null)
        {
            return;
        }

        AnalyzeQueryEfMutation(context, invocation, method);
        AnalyzeEndpointRouteMapping(context, invocation, method);
    }

    private static void AnalyzeQueryEfMutation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType is null || !IsQueryHandler(containingType))
        {
            return;
        }

        if (!IsEfMutation(method))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.QueryEfMutation,
            invocation.GetLocation(),
            containingType.Name,
            method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsEfMutation(IMethodSymbol method)
    {
        if (EfMutationMethods.Contains(method.Name) && IsEfPersistenceType(method.ContainingType))
        {
            return true;
        }

        return EfExtensionMutationMethods.Contains(method.Name) && IsEfCoreNamespace(method.ContainingType);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType is null || !IsQueryHandler(containingType))
        {
            return;
        }

        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is not IPropertySymbol property)
        {
            return;
        }

        // Setting EntityEntry.State (e.g. _db.Entry(x).State = EntityState.Deleted) is a
        // change-tracker mutation that bypasses the Add/Update/Remove APIs.
        if (property.Name == "State" && IsEntityEntryType(property.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.QueryEfMutation,
                assignment.GetLocation(),
                containingType.Name,
                "EntityEntry.State"));
        }
    }

    private static void AnalyzeEndpointRouteMapping(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (!RouteMappingMethods.Contains(method.Name))
        {
            return;
        }

        if (!IsInsideEndpointAdapter(context.ContainingSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.EndpointAdapterShape,
                invocation.GetLocation(),
                $"Route mapping '{method.Name}' must be declared inside a type or method marked with EndpointAdapterAttribute."));
            return;
        }

        foreach (var endpointMethod in ResolveEndpointMethods(context.SemanticModel, invocation, context.CancellationToken))
        {
            AnalyzeEndpointMethodParameters(context, endpointMethod);
        }
    }

    private static IEnumerable<IMethodSymbol> ResolveEndpointMethods(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is IdentifierNameSyntax or MemberAccessExpressionSyntax)
            {
                if (semanticModel.GetSymbolInfo(argument.Expression, cancellationToken).Symbol is IMethodSymbol method)
                {
                    yield return method;
                }
            }

            if (argument.Expression is ParenthesizedLambdaExpressionSyntax lambda)
            {
                var lambdaSymbol = semanticModel.GetSymbolInfo(lambda, cancellationToken).Symbol as IMethodSymbol;
                if (lambdaSymbol is not null)
                {
                    yield return lambdaSymbol;
                }
            }
        }
    }

    private static void AnalyzeEndpointMethodParameters(SyntaxNodeAnalysisContext context, IMethodSymbol endpointMethod)
    {
        var typedHandlerCount = 0;

        foreach (var parameter in endpointMethod.Parameters)
        {
            if (IsCancellationToken(parameter.Type) || IsBoundInput(parameter.Type))
            {
                continue;
            }

            if (IsTypedHandler(parameter.Type))
            {
                typedHandlerCount++;
                if (typedHandlerCount > 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.EndpointAdapterShape,
                        parameter.Locations.FirstOrDefault(),
                        $"Endpoint handler '{endpointMethod.Name}' may inject only one typed command/query handler."));
                }

                continue;
            }

            if (IsForbiddenEndpointDependency(parameter.Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.EndpointAdapterShape,
                    parameter.Locations.FirstOrDefault(),
                    $"Endpoint handler '{endpointMethod.Name}' may not depend on forbidden service '{parameter.Type.ToDisplayString()}'."));
            }
        }
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (LooksLikePersistenceWrapper(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.GenericPersistenceWrapper,
                type.Locations.FirstOrDefault(),
                type.Name));
        }
    }

    private static bool LooksLikePersistenceWrapper(INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsStatic)
        {
            return false;
        }

        // Both generic (Repository<T>) and hand-written non-generic (OrderRepository) wrappers
        // are in scope. The signal is an EF dependency plus either a wrapper-ish name or a
        // cluster of CRUD members.
        var explicitlyNamed = ContainsAny(type.Name, "Repository", "UnitOfWork", "CrudService", "GenericService");
        var hasEfDependency = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Constructor)
            .SelectMany(method => method.Parameters.Select(parameter => parameter.Type))
            .Concat(type.GetMembers().OfType<IFieldSymbol>().Select(field => field.Type))
            .Concat(type.GetMembers().OfType<IPropertySymbol>().Select(property => property.Type))
            .Any(IsEfPersistenceType);

        var crudMemberCount = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Count(method => CrudMemberNames.Contains(method.Name));

        return hasEfDependency && (explicitlyNamed || crudMemberCount >= 3);
    }

    private static bool IsInsideEndpointAdapter(ISymbol? symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (HasEndpointAdapterAttribute(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEndpointAdapterAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute =>
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
            {
                return false;
            }

            var name = attributeClass.Name;
            var fullName = attributeClass.ToDisplayString();
            return name is "EndpointAdapterAttribute" or "EndpointAdapter" ||
                   fullName.EndsWith(".EndpointAdapterAttribute", StringComparison.Ordinal);
        });
    }

    private static bool IsQueryHandler(INamedTypeSymbol type)
    {
        return type.Name.EndsWith("QueryHandler", StringComparison.Ordinal) ||
               type.AllInterfaces.Any(interfaceType => interfaceType.Name.StartsWith("IQueryHandler", StringComparison.Ordinal));
    }

    private static bool IsEfPersistenceType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            var fullName = current.OriginalDefinition.ToDisplayString();
            if (fullName is "Microsoft.EntityFrameworkCore.DbContext" ||
                fullName.StartsWith("Microsoft.EntityFrameworkCore.DbSet<", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEfCoreNamespace(ISymbol? symbol)
    {
        var namespaceName = symbol?.ContainingNamespace?.ToDisplayString();
        return namespaceName is not null &&
               (namespaceName == "Microsoft.EntityFrameworkCore" ||
                namespaceName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal));
    }

    private static bool IsEntityEntryType(ITypeSymbol type)
    {
        var fullName = type.OriginalDefinition.ToDisplayString();
        return fullName == "Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry" ||
               fullName.StartsWith("Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<", StringComparison.Ordinal);
    }

    private static bool IsForbiddenEndpointDependency(ITypeSymbol type)
    {
        var fullName = type.OriginalDefinition.ToDisplayString();

        return ExplicitForbiddenTypeNames.Contains(fullName) ||
               fullName is "Microsoft.EntityFrameworkCore.DbContext" ||
               fullName.StartsWith("Microsoft.EntityFrameworkCore.DbSet<", StringComparison.Ordinal) ||
               fullName.EndsWith(".IHttpContextAccessor", StringComparison.Ordinal) ||
               fullName.EndsWith(".IAuthorizationService", StringComparison.Ordinal);
    }

    private static bool IsTypedHandler(ITypeSymbol type)
    {
        return type.Name.EndsWith("CommandHandler", StringComparison.Ordinal) ||
               type.Name.EndsWith("QueryHandler", StringComparison.Ordinal);
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.ToDisplayString() == "System.Threading.CancellationToken";
    }

    private static bool IsBoundInput(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        var fullName = type.ToDisplayString();
        return fullName is
            "bool" or
            "byte" or
            "short" or
            "int" or
            "long" or
            "float" or
            "double" or
            "decimal" or
            "string" or
            "System.Guid" or
            "System.DateOnly" or
            "System.DateTime" or
            "System.DateTimeOffset" ||
            type.Name.EndsWith("Command", StringComparison.Ordinal) ||
            type.Name.EndsWith("Query", StringComparison.Ordinal) ||
            type.Name.EndsWith("Request", StringComparison.Ordinal) ||
            type.Name.EndsWith("Dto", StringComparison.Ordinal);
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}
