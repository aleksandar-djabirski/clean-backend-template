using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Product.Guardrails.Analyzers;

var tests = new (string Name, Action Body)[]
{
    ("PGB001 does not flag ordinary collections", Pgb001AllowsCollections),
    ("PGB001 flags EF Core mutation in query handlers and private helpers", Pgb001FlagsEfMutation),
    ("PGB006 allows endpoint adapters with one typed handler", Pgb006AllowsValidEndpoint),
    ("PGB006 blocks route mappings outside endpoint adapters", Pgb006BlocksRouteMappingOutsideAdapter),
    ("PGB006 blocks forbidden endpoint dependencies", Pgb006BlocksForbiddenDependencies),
    ("PGB003 flags explicit generic repository wrappers", Pgb003FlagsGenericRepository),
};

foreach (var test in tests)
{
    test.Body();
    Console.WriteLine($"PASS {test.Name}");
}

static void Pgb001AllowsCollections()
{
    var diagnostics = Analyze("""
        using System.Collections.Generic;

        internal sealed class GetThingQueryHandler
        {
            public void Handle()
            {
                var list = new List<string>();
                var dictionary = new Dictionary<string, string>();
                var set = new HashSet<string>();
                list.Add("a");
                dictionary.Remove("a");
                set.Clear();
            }
        }
        """);

    AssertNoDiagnostic(diagnostics, "PGB001");
}

static void Pgb001FlagsEfMutation()
{
    var diagnostics = Analyze(FixtureStubs() + """
        internal sealed class GetThingQueryHandler
        {
            private readonly Microsoft.EntityFrameworkCore.DbContext _db = new();
            private readonly Microsoft.EntityFrameworkCore.DbSet<Thing> _things = new();

            public void Handle() => Mutate();

            private void Mutate()
            {
                _things.Add(new Thing());
                _db.SaveChanges();
            }
        }

        internal sealed class Thing;
        """);

    AssertDiagnosticCount(diagnostics, "PGB001", 2);
}

static void Pgb006AllowsValidEndpoint()
{
    var diagnostics = Analyze(EndpointStubs() + """
        [EndpointAdapter]
        internal static class RequestsEndpoints
        {
            public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
            {
                Microsoft.AspNetCore.Routing.EndpointRouteBuilderExtensions.MapGet(
                    app,
                    "/requests/{id}",
                    (GetRequestQuery route, GetRequestQueryHandler handler, System.Threading.CancellationToken cancellationToken) => "ok");
            }
        }

        internal sealed record GetRequestQuery(System.Guid Id);
        internal sealed class GetRequestQueryHandler;
        """);

    AssertNoDiagnostic(diagnostics, "PGB006");
}

static void Pgb006BlocksRouteMappingOutsideAdapter()
{
    var diagnostics = Analyze(EndpointStubs() + """
        internal static class RequestsEndpoints
        {
            public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
            {
                Microsoft.AspNetCore.Routing.EndpointRouteBuilderExtensions.MapGet(app, "/requests", () => "ok");
            }
        }
        """);

    AssertDiagnosticCount(diagnostics, "PGB006", 1);
}

static void Pgb006BlocksForbiddenDependencies()
{
    var diagnostics = Analyze(EndpointStubs() + """
        [EndpointAdapter]
        internal static class RequestsEndpoints
        {
            public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
            {
                Microsoft.AspNetCore.Routing.EndpointRouteBuilderExtensions.MapGet(
                    app,
                    "/requests",
                    (Microsoft.Extensions.Configuration.IConfiguration configuration, GetRequestQueryHandler handler) => "ok");
            }
        }

        internal sealed class GetRequestQueryHandler;
        """);

    AssertDiagnosticCount(diagnostics, "PGB006", 1);
}

static void Pgb003FlagsGenericRepository()
{
    var diagnostics = Analyze(FixtureStubs() + """
        internal sealed class Repository<TEntity>
        {
            private readonly Microsoft.EntityFrameworkCore.DbSet<TEntity> _set = new();
            public void Add(TEntity entity) => _set.Add(entity);
            public void Remove(TEntity entity) => _set.Remove(entity);
        }
        """);

    AssertDiagnosticCount(diagnostics, "PGB003", 1);
}

static ImmutableArray<Diagnostic> Analyze(string source)
{
    var compilation = CSharpCompilation.Create(
        "AnalyzerFixture",
        new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)) },
        TrustedPlatformReferences(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    var compilerDiagnostics = compilation.GetDiagnostics()
        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        .ToArray();

    if (compilerDiagnostics.Length > 0)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, compilerDiagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ProductGuardrailsAnalyzer()))
        .GetAnalyzerDiagnosticsAsync()
        .GetAwaiter()
        .GetResult();
}

static MetadataReference[] TrustedPlatformReferences()
{
    return ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
        .Split(Path.PathSeparator)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray() ?? Array.Empty<MetadataReference>();
}

static void AssertNoDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id)
{
    var matches = diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
    if (matches.Length != 0)
    {
        throw new InvalidOperationException($"Expected no {id} diagnostics but found {matches.Length}:{Environment.NewLine}{string.Join(Environment.NewLine, matches.Select(diagnostic => diagnostic.ToString()))}");
    }
}

static void AssertDiagnosticCount(ImmutableArray<Diagnostic> diagnostics, string id, int expected)
{
    var matches = diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
    if (matches.Length != expected)
    {
        throw new InvalidOperationException($"Expected {expected} {id} diagnostics but found {matches.Length}:{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()))}");
    }
}

static string FixtureStubs() => """
    namespace Microsoft.EntityFrameworkCore
    {
        public class DbContext
        {
            public int SaveChanges() => 0;
            public System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
        }

        public class DbSet<TEntity>
        {
            public void Add(TEntity entity) { }
            public void Update(TEntity entity) { }
            public void Remove(TEntity entity) { }
            public void Clear() { }
        }
    }
    """;

static string EndpointStubs() => """
    using System;

    internal sealed class EndpointAdapterAttribute : Attribute;

    namespace Microsoft.AspNetCore.Routing
    {
        public interface IEndpointRouteBuilder { }

        public static class EndpointRouteBuilderExtensions
        {
            public static void MapGet(this IEndpointRouteBuilder app, string pattern, Delegate handler) { }
        }
    }

    namespace Microsoft.Extensions.Configuration
    {
        public interface IConfiguration { }
    }
    """;
