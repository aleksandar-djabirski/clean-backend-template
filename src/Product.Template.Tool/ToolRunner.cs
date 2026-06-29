using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Product.Template.Tool;

public static class ToolRunner
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            WriteHelp(output);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var command = args[0];
            var options = ParseOptions(args.Skip(1));

            return command switch
            {
                "bootstrap" => await BootstrapAsync(options, output),
                "new-feature" => await NewFeatureAsync(options, output),
                "verify" => await VerifyAsync(options, output, error),
                _ => Fail(error, $"Unknown command '{command}'.")
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> BootstrapAsync(IReadOnlyDictionary<string, string> options, TextWriter output)
    {
        var productName = Required(options, "name");
        var moduleName = Required(options, "module");
        var outputDirectory = Path.GetFullPath(options.GetValueOrDefault("output", Directory.GetCurrentDirectory()));
        var productDirectory = Path.Combine(outputDirectory, productName);
        var apiDirectory = Path.Combine(productDirectory, "src", $"{productName}.Api");
        var abstractionsDirectory = Path.Combine(productDirectory, "src", $"{productName}.Abstractions");
        var moduleDirectory = Path.Combine(apiDirectory, "Modules", moduleName);
        var analyzerAssembly = FindTemplateRoot() is { } templateRoot
            ? Path.Combine(templateRoot, "src", "Product.Guardrails.Analyzers", "bin", "Debug", "net10.0", "Product.Guardrails.Analyzers.dll")
            : null;

        if (Directory.Exists(productDirectory))
        {
            throw new InvalidOperationException($"Product directory already exists: {productDirectory}");
        }

        Directory.CreateDirectory(moduleDirectory);
        Directory.CreateDirectory(abstractionsDirectory);
        Directory.CreateDirectory(Path.Combine(apiDirectory, "Guardrails"));
        Directory.CreateDirectory(Path.Combine(productDirectory, "docs", "modules"));
        Directory.CreateDirectory(Path.Combine(productDirectory, "eng"));

        await File.WriteAllTextAsync(Path.Combine(productDirectory, "global.json"), """
        {
          "sdk": {
            "version": "10.0.301",
            "rollForward": "disable"
          }
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "Directory.Build.props"), """
        <Project>
          <PropertyGroup>
            <LangVersion>latest</LangVersion>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
          </PropertyGroup>
        </Project>
        """);
        await File.WriteAllTextAsync(Path.Combine(productDirectory, $"{productName}.slnx"), SolutionFile(productName));
        await File.WriteAllTextAsync(Path.Combine(abstractionsDirectory, $"{productName}.Abstractions.csproj"), AbstractionsProjectFile());
        await File.WriteAllTextAsync(Path.Combine(abstractionsDirectory, "Error.cs"), ErrorFile(productName));
        await File.WriteAllTextAsync(Path.Combine(abstractionsDirectory, "Result.cs"), ResultFile(productName));
        await File.WriteAllTextAsync(Path.Combine(abstractionsDirectory, "Validation.cs"), ValidationFile(productName));
        await File.WriteAllTextAsync(Path.Combine(apiDirectory, $"{productName}.Api.csproj"), ApiProjectFile(productName, analyzerAssembly));
        await File.WriteAllTextAsync(Path.Combine(apiDirectory, "Program.cs"), ProgramFile(productName, moduleName));
        await File.WriteAllTextAsync(Path.Combine(apiDirectory, "Guardrails", "EndpointAdapterAttribute.cs"), EndpointAdapterAttributeFile(productName));
        await File.WriteAllTextAsync(Path.Combine(moduleDirectory, $"{moduleName}Module.cs"), ModuleFile(productName, moduleName));
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "ownership-map.md"), OwnershipMapFile(productName, moduleName));
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "modules", $"{moduleName}.capabilities.md"), ModuleCapabilityMapFile(moduleName));
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "guardrail-exceptions.md"), GuardrailExceptionsDocFile());
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "migration-safety-profile.md"), MigrationSafetyProfileFile());
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "observability-baseline.md"), ObservabilityBaselineFile());
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "eng", "guardrail-exceptions.json"), GuardrailExceptionsConfigFile());
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "eng", "package-policy.json"), PackagePolicyConfigFile());

        output.WriteLine($"Bootstrapped {productName} at {productDirectory}");
        return 0;
    }

    private static async Task<int> NewFeatureAsync(IReadOnlyDictionary<string, string> options, TextWriter output)
    {
        var productDirectory = Path.GetFullPath(Required(options, "product"));
        var moduleName = Required(options, "module");
        var featureName = Required(options, "name");
        var kind = Required(options, "kind").ToLowerInvariant();

        var productName = new DirectoryInfo(productDirectory).Name;
        var apiDirectory = Path.Combine(productDirectory, "src", $"{productName}.Api");
        var featureDirectory = Path.Combine(apiDirectory, "Modules", moduleName, featureName);
        Directory.CreateDirectory(featureDirectory);

        switch (kind)
        {
            case "command":
                await File.WriteAllTextAsync(Path.Combine(featureDirectory, $"{featureName}Command.cs"), CommandFile(productName, moduleName, featureName));
                await File.WriteAllTextAsync(Path.Combine(featureDirectory, $"{featureName}CommandHandler.cs"), CommandHandlerFile(productName, moduleName, featureName));
                await File.WriteAllTextAsync(Path.Combine(featureDirectory, $"{featureName}Endpoint.cs"), CommandEndpointFile(productName, moduleName, featureName));
                break;
            case "query":
                await File.WriteAllTextAsync(Path.Combine(featureDirectory, $"{featureName}Query.cs"), QueryFile(productName, moduleName, featureName));
                await File.WriteAllTextAsync(Path.Combine(featureDirectory, $"{featureName}QueryHandler.cs"), QueryHandlerFile(productName, moduleName, featureName));
                await File.WriteAllTextAsync(Path.Combine(featureDirectory, $"{featureName}Endpoint.cs"), QueryEndpointFile(productName, moduleName, featureName));
                break;
            case "weather-query":
                await File.WriteAllTextAsync(Path.Combine(featureDirectory, $"{featureName}Query.cs"), WeatherQueryFile(productName, moduleName, featureName));
                await File.WriteAllTextAsync(Path.Combine(featureDirectory, $"{featureName}QueryHandler.cs"), WeatherQueryHandlerFile(productName, moduleName, featureName));
                await File.WriteAllTextAsync(Path.Combine(featureDirectory, $"{featureName}Endpoint.cs"), WeatherQueryEndpointFile(productName, moduleName, featureName));
                break;
            default:
                throw new InvalidOperationException("--kind must be 'command', 'query', or 'weather-query'.");
        }

        await UpdateModuleFileAsync(apiDirectory, productName, moduleName);

        output.WriteLine($"Scaffolded {kind} {featureName} in module {moduleName}");
        return 0;
    }

    private static async Task<int> VerifyAsync(IReadOnlyDictionary<string, string> options, TextWriter output, TextWriter error)
    {
        var productDirectory = Path.GetFullPath(Required(options, "product"));
        var problems = VerifyStructure(productDirectory);
        var productName = new DirectoryInfo(productDirectory).Name;
        var apiProject = Path.Combine(productDirectory, "src", $"{productName}.Api", $"{productName}.Api.csproj");

        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                error.WriteLine(problem);
            }

            return 1;
        }

        var dotnet = FindDotnet();
        return await RunProcessAsync(dotnet, $"build \"{apiProject}\"", productDirectory, output, error);
    }

    private static List<string> VerifyStructure(string productDirectory)
    {
        var problems = new List<string>();
        if (!Directory.Exists(productDirectory))
        {
            problems.Add($"Product directory does not exist: {productDirectory}");
            return problems;
        }

        var productName = new DirectoryInfo(productDirectory).Name;
        var apiProject = Path.Combine(productDirectory, "src", $"{productName}.Api", $"{productName}.Api.csproj");
        var abstractionsProject = Path.Combine(productDirectory, "src", $"{productName}.Abstractions", $"{productName}.Abstractions.csproj");
        if (!File.Exists(apiProject))
        {
            problems.Add($"Missing API project: {apiProject}");
            return problems;
        }

        if (!File.Exists(abstractionsProject))
        {
            problems.Add($"Missing abstractions project: {abstractionsProject}");
        }

        var projectText = File.ReadAllText(apiProject);
        if (!projectText.Contains("Product.Guardrails.Analyzers.dll", StringComparison.Ordinal))
        {
            problems.Add("API project must reference Product.Guardrails.Analyzers.dll as an analyzer.");
        }

        if (!projectText.Contains($"{productName}.Abstractions.csproj", StringComparison.Ordinal))
        {
            problems.Add($"API project must reference {productName}.Abstractions.");
        }

        problems.AddRange(VerifyRequiredFiles(productDirectory, productName));
        problems.AddRange(VerifyModuleBoundaries(productDirectory, productName));

        var unresolved = Directory.EnumerateFiles(productDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("{{", StringComparison.Ordinal) ||
                           File.ReadAllText(path).Contains("TODO_TEMPLATE", StringComparison.Ordinal))
            .ToArray();

        if (unresolved.Length > 0)
        {
            problems.Add($"Unresolved placeholders remain: {string.Join(", ", unresolved)}");
        }

        return problems;
    }

    private static IEnumerable<string> VerifyRequiredFiles(string productDirectory, string productName)
    {
        var requiredFiles = new[]
        {
            Path.Combine(productDirectory, "docs", "ownership-map.md"),
            Path.Combine(productDirectory, "docs", "guardrail-exceptions.md"),
            Path.Combine(productDirectory, "docs", "migration-safety-profile.md"),
            Path.Combine(productDirectory, "docs", "observability-baseline.md"),
            Path.Combine(productDirectory, "eng", "guardrail-exceptions.json"),
            Path.Combine(productDirectory, "eng", "package-policy.json")
        };

        foreach (var file in requiredFiles)
        {
            if (!File.Exists(file))
            {
                yield return $"Missing required Phase 1 file: {file}";
            }
        }

        var modulesDirectory = Path.Combine(productDirectory, "src", $"{productName}.Api", "Modules");
        if (!Directory.Exists(modulesDirectory))
        {
            yield return $"Missing modules directory: {modulesDirectory}";
            yield break;
        }

        foreach (var moduleDirectory in Directory.GetDirectories(modulesDirectory))
        {
            var moduleName = Path.GetFileName(moduleDirectory);
            var moduleFile = Path.Combine(moduleDirectory, $"{moduleName}Module.cs");
            var capabilityFile = Path.Combine(productDirectory, "docs", "modules", $"{moduleName}.capabilities.md");

            if (!File.Exists(moduleFile))
            {
                yield return $"Missing module facade: {moduleFile}";
            }

            if (!File.Exists(capabilityFile))
            {
                yield return $"Missing module capability map: {capabilityFile}";
            }
        }
    }

    private static IEnumerable<string> VerifyModuleBoundaries(string productDirectory, string productName)
    {
        var modulesDirectory = Path.Combine(productDirectory, "src", $"{productName}.Api", "Modules");
        if (!Directory.Exists(modulesDirectory))
        {
            yield break;
        }

        var publicTypePattern = new Regex(@"\bpublic\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*\b(class|record|struct|interface)\b", RegexOptions.Compiled);
        foreach (var file in Directory.EnumerateFiles(modulesDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.EndsWith("Module.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (publicTypePattern.IsMatch(text))
            {
                yield return $"Module implementation types must be internal by default: {file}";
            }
        }
    }

    private static async Task UpdateModuleFileAsync(string apiDirectory, string productName, string moduleName)
    {
        var moduleFile = Path.Combine(apiDirectory, "Modules", moduleName, $"{moduleName}Module.cs");
        var featureDirectories = Directory.GetDirectories(Path.Combine(apiDirectory, "Modules", moduleName))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        await File.WriteAllTextAsync(moduleFile, ModuleFile(productName, moduleName, featureDirectories!));
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, string workingDirectory, TextWriter output, TextWriter error)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                error.WriteLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pendingName = string.Empty;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                pendingName = arg[2..];
                result[pendingName] = "true";
                continue;
            }

            if (pendingName.Length == 0)
            {
                throw new InvalidOperationException($"Unexpected argument '{arg}'.");
            }

            result[pendingName] = arg;
            pendingName = string.Empty;
        }

        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name)
    {
        return options.TryGetValue(name, out var value) && value != "true" && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Missing required option --{name}.");
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 1;
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("Product.Template.Tool commands:");
        output.WriteLine("  bootstrap --name SampleProduct --module Requests --output /tmp");
        output.WriteLine("  new-feature --product /tmp/SampleProduct --module Requests --kind command --name CreateRequest");
        output.WriteLine("  new-feature --product /tmp/SampleProduct --module Requests --kind query --name GetRequest");
        output.WriteLine("  new-feature --product /tmp/SampleProduct --module Weather --kind weather-query --name GetWeatherForecast");
        output.WriteLine("  verify --product /tmp/SampleProduct");
    }

    private static string FindDotnet()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath)
        {
            return hostPath;
        }

        if (FindTemplateRoot() is { } templateRoot)
        {
            var localDotnet = Path.Combine(templateRoot, ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(localDotnet))
            {
                return localDotnet;
            }
        }

        return "dotnet";
    }

    private static string? FindTemplateRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            for (var directory = new DirectoryInfo(candidate); directory is not null; directory = directory.Parent)
            {
                var analyzerProject = Path.Combine(directory.FullName, "src", "Product.Guardrails.Analyzers", "Product.Guardrails.Analyzers.csproj");
                if (File.Exists(analyzerProject))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }

    private static string SolutionFile(string productName)
    {
        return $$"""
        <Solution>
          <Project Path="src/{{productName}}.Abstractions/{{productName}}.Abstractions.csproj" />
          <Project Path="src/{{productName}}.Api/{{productName}}.Api.csproj" />
        </Solution>
        """;
    }

    private static string AbstractionsProjectFile()
    {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
          </PropertyGroup>
        </Project>
        """;
    }

    private static string ApiProjectFile(string productName, string? analyzerAssembly)
    {
        var analyzerReference = analyzerAssembly is null
            ? string.Empty
            : $"""
                <Analyzer Include="{EscapeXml(analyzerAssembly)}" />
            """;

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <WarningsAsErrors>$(WarningsAsErrors);PGB001;PGB006</WarningsAsErrors>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="../{{productName}}.Abstractions/{{productName}}.Abstractions.csproj" />
        {{analyzerReference}}
          </ItemGroup>
        </Project>
        """;
    }

    private static string ErrorFile(string productName)
    {
        return $$"""
        namespace {{productName}}.Abstractions;

        public sealed record Error(string Code, string Message)
        {
            public static Error None { get; } = new(string.Empty, string.Empty);
        }
        """;
    }

    private static string ResultFile(string productName)
    {
        return $$"""
        namespace {{productName}}.Abstractions;

        public class Result
        {
            protected Result(bool isSuccess, Error error)
            {
                if (isSuccess && error != Error.None)
                {
                    throw new ArgumentException("Successful results cannot carry an error.", nameof(error));
                }

                if (!isSuccess && error == Error.None)
                {
                    throw new ArgumentException("Failed results must carry an error.", nameof(error));
                }

                IsSuccess = isSuccess;
                Error = error;
            }

            public bool IsSuccess { get; }

            public bool IsFailure => !IsSuccess;

            public Error Error { get; }

            public static Result Success() => new(true, Error.None);

            public static Result Failure(Error error) => new(false, error);

            public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

            public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
        }

        public sealed class Result<TValue> : Result
        {
            private readonly TValue? _value;

            internal Result(TValue? value, bool isSuccess, Error error)
                : base(isSuccess, error)
            {
                _value = value;
            }

            public TValue Value => IsSuccess
                ? _value!
                : throw new InvalidOperationException("Cannot access the value of a failed result.");
        }
        """;
    }

    private static string ValidationFile(string productName)
    {
        return $$"""
        namespace {{productName}}.Abstractions;

        public interface IValidator<in TRequest>
        {
            ValueTask<IReadOnlyList<Error>> ValidateAsync(TRequest request, CancellationToken cancellationToken);
        }

        public static class ValidationResult
        {
            public static IReadOnlyList<Error> Valid { get; } = Array.Empty<Error>();
        }
        """;
    }

    private static string ProgramFile(string productName, string moduleName)
    {
        return $$"""
        using {{productName}}.Api.Modules.{{moduleName}};

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Add{{moduleName}}Module();

        var app = builder.Build();
        app.Map{{moduleName}}Endpoints();
        app.Run();

        public partial class Program;
        """;
    }

    private static string EndpointAdapterAttributeFile(string productName)
    {
        return $$"""
        namespace {{productName}}.Api.Guardrails;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
        internal sealed class EndpointAdapterAttribute : Attribute;
        """;
    }

    private static string ModuleFile(string productName, string moduleName, params string[] features)
    {
        var usings = string.Join(Environment.NewLine, features.Select(feature => $"using {productName}.Api.Modules.{moduleName}.{feature};"));
        var serviceRegistrations = string.Join(Environment.NewLine, features.Select(feature => $"        services.AddScoped<{feature}{HandlerSuffix(feature)}>();"));
        var endpointMappings = string.Join(Environment.NewLine, features.Select(feature => $"        endpoints.Map{feature}Endpoint();"));

        return $$"""
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.DependencyInjection;
        {{usings}}

        namespace {{productName}}.Api.Modules.{{moduleName}};

        internal static class {{moduleName}}Module
        {
            public static IServiceCollection Add{{moduleName}}Module(this IServiceCollection services)
            {
        {{serviceRegistrations}}
                return services;
            }

            public static IEndpointRouteBuilder Map{{moduleName}}Endpoints(this IEndpointRouteBuilder endpoints)
            {
        {{endpointMappings}}
                return endpoints;
            }
        }
        """;
    }

    private static string HandlerSuffix(string featureName)
    {
        if (featureName.StartsWith("Get", StringComparison.Ordinal) ||
            featureName.StartsWith("List", StringComparison.Ordinal) ||
            featureName.StartsWith("Find", StringComparison.Ordinal))
        {
            return "QueryHandler";
        }

        return "CommandHandler";
    }

    private static string CommandFile(string productName, string moduleName, string featureName)
    {
        return $$"""
        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        internal sealed record {{featureName}}Command(string Name);
        """;
    }

    private static string CommandHandlerFile(string productName, string moduleName, string featureName)
    {
        return $$"""
        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        internal sealed class {{featureName}}CommandHandler
        {
            public Task<Guid> HandleAsync({{featureName}}Command command, CancellationToken cancellationToken)
            {
                return Task.FromResult(Guid.NewGuid());
            }
        }
        """;
    }

    private static string CommandEndpointFile(string productName, string moduleName, string featureName)
    {
        var route = ToKebabCase(featureName.Replace("Create", string.Empty, StringComparison.Ordinal));

        return $$"""
        using {{productName}}.Api.Guardrails;
        using Microsoft.AspNetCore.Mvc;

        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        [EndpointAdapter]
        internal static class {{featureName}}Endpoint
        {
            public static IEndpointRouteBuilder Map{{featureName}}Endpoint(this IEndpointRouteBuilder endpoints)
            {
                endpoints.MapPost("/{{route}}", HandleAsync);
                return endpoints;
            }

            private static async Task<IResult> HandleAsync(
                [FromBody] {{featureName}}Command command,
                {{featureName}}CommandHandler handler,
                CancellationToken cancellationToken)
            {
                var id = await handler.HandleAsync(command, cancellationToken);
                return Results.Created($"/{{route}}/{id}", new { id });
            }
        }
        """;
    }

    private static string QueryFile(string productName, string moduleName, string featureName)
    {
        return $$"""
        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        internal sealed record {{featureName}}Query(Guid Id);

        internal sealed record {{featureName}}Response(Guid Id, string Name);
        """;
    }

    private static string QueryHandlerFile(string productName, string moduleName, string featureName)
    {
        return $$"""
        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        internal sealed class {{featureName}}QueryHandler
        {
            public Task<{{featureName}}Response> HandleAsync({{featureName}}Query query, CancellationToken cancellationToken)
            {
                return Task.FromResult(new {{featureName}}Response(query.Id, "sample"));
            }
        }
        """;
    }

    private static string QueryEndpointFile(string productName, string moduleName, string featureName)
    {
        var route = ToKebabCase(featureName.Replace("Get", string.Empty, StringComparison.Ordinal));

        return $$"""
        using {{productName}}.Api.Guardrails;

        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        [EndpointAdapter]
        internal static class {{featureName}}Endpoint
        {
            public static IEndpointRouteBuilder Map{{featureName}}Endpoint(this IEndpointRouteBuilder endpoints)
            {
                endpoints.MapGet("/{{route}}/{id:guid}", HandleAsync);
                return endpoints;
            }

            private static async Task<IResult> HandleAsync(
                Guid id,
                {{featureName}}QueryHandler handler,
                CancellationToken cancellationToken)
            {
                var response = await handler.HandleAsync(new {{featureName}}Query(id), cancellationToken);
                return Results.Ok(response);
            }
        }
        """;
    }

    private static string WeatherQueryFile(string productName, string moduleName, string featureName)
    {
        return $$"""
        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        internal sealed record {{featureName}}Query;

        internal sealed record WeatherForecast(DateOnly Date, int TemperatureC, string Summary)
        {
            public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
        }
        """;
    }

    private static string WeatherQueryHandlerFile(string productName, string moduleName, string featureName)
    {
        return $$"""
        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        internal sealed class {{featureName}}QueryHandler
        {
            private static readonly string[] Summaries =
            [
                "Freezing",
                "Bracing",
                "Chilly",
                "Cool",
                "Mild",
                "Warm",
                "Balmy",
                "Hot",
                "Sweltering",
                "Scorching"
            ];

            public Task<IReadOnlyList<WeatherForecast>> HandleAsync({{featureName}}Query query, CancellationToken cancellationToken)
            {
                var forecasts = Enumerable.Range(1, 5)
                    .Select(index => new WeatherForecast(
                        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(index)),
                        Random.Shared.Next(-20, 55),
                        Summaries[Random.Shared.Next(Summaries.Length)]))
                    .ToArray();

                return Task.FromResult<IReadOnlyList<WeatherForecast>>(forecasts);
            }
        }
        """;
    }

    private static string WeatherQueryEndpointFile(string productName, string moduleName, string featureName)
    {
        return $$"""
        using {{productName}}.Api.Guardrails;

        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        [EndpointAdapter]
        internal static class {{featureName}}Endpoint
        {
            public static IEndpointRouteBuilder Map{{featureName}}Endpoint(this IEndpointRouteBuilder endpoints)
            {
                endpoints.MapGet("/weatherforecast", HandleAsync);
                return endpoints;
            }

            private static async Task<IResult> HandleAsync(
                {{featureName}}QueryHandler handler,
                CancellationToken cancellationToken)
            {
                var response = await handler.HandleAsync(new {{featureName}}Query(), cancellationToken);
                return Results.Ok(response);
            }
        }
        """;
    }

    private static string OwnershipMapFile(string productName, string moduleName)
    {
        return $$"""
        # Ownership Map

        Product: {{productName}}

        This file records module ownership evidence. Keep entries short and update only when ownership changes.

        | Module | Owns | Does not own | Escalation trigger |
        | --- | --- | --- | --- |
        | {{moduleName}} | Initial generated module capability. | Cross-module workflows and shared technical abstractions. | Unclear ownership, semantic duplication, permissions, tenancy, money, compliance, or irreversible lifecycle behavior. |
        """;
    }

    private static string ModuleCapabilityMapFile(string moduleName)
    {
        return $$"""
        # {{moduleName}} Capability Map

        Owning module: {{moduleName}}

        ## Capabilities

        | Capability | Commands | Queries | Notes |
        | --- | --- | --- | --- |
        | Generated smoke capability | Generated command scaffold. | Generated query scaffold. | Replace with real product capability evidence when the product is implemented. |

        ## Change Evidence Template

        ```text
        Change:
        Owning module:
        Ownership search:
          Search terms:
          Paths inspected:
          Existing behavior found:
        Decision:
          Reuse / Extend / New capability / Cross-module contract / Escalation

        Command or query:
        Tests added or changed:
        Verification result:
        ```
        """;
    }

    private static string GuardrailExceptionsDocFile()
    {
        return """
        # Guardrail Exceptions

        Guardrail exceptions are rare, local, and temporary by default.

        Required evidence:

        - Rule id or protected asset.
        - Owning module.
        - Reason the normal shape cannot work.
        - Tests proving the exception remains safe.
        - Expiry or review condition.

        Do not weaken an analyzer or architecture rule globally to unblock one feature.
        """;
    }

    private static string MigrationSafetyProfileFile()
    {
        return """
        # Migration Safety Profile

        Choose one profile before the first production deployment with persistent data.

        ## Maintenance Window

        Use when coordinated downtime is acceptable. Document who runs migrations, sequencing, rollback expectation, and when incompatible application instances are stopped.

        ## Rolling Compatible

        Use when old and new application instances may run against the database during deployment. Use expand-contract changes and test representative prior-schema migrations for destructive or compatibility-sensitive changes.

        Selected profile: undecided before production persistence.
        """;
    }

    private static string ObservabilityBaselineFile()
    {
        return """
        # Vendor-Neutral Observability Baseline

        The baseline uses built-in .NET primitives and avoids vendor SDKs.

        Required conventions:

        - W3C trace-context propagation.
        - Activity-compatible tracing.
        - Correlation id accepted from callers or generated at the edge.
        - Structured request-completion logging with trace id, correlation id, method, route template, status code, and elapsed duration.
        - Structured unhandled-exception logging.
        - Health endpoints.
        - Trace or correlation identifiers in safe error responses.
        - No secrets, authorization headers, access tokens, payment data, personal data, or raw sensitive request payloads logged by default.

        Exporters, dashboards, alerting destinations, background-job propagation, and distributed tracing packages are product decisions, not template defaults.
        """;
    }

    private static string GuardrailExceptionsConfigFile()
    {
        return """
        {
          "exceptions": []
        }
        """;
    }

    private static string PackagePolicyConfigFile()
    {
        return """
        {
          "lockedRestoreRequired": true,
          "prohibitedPackages": [
            "Microsoft.EntityFrameworkCore.Proxies"
          ],
          "requiresDecisionRecord": [
            "provider SDKs",
            "observability exporters",
            "message brokers",
            "background jobs",
            "caching providers"
          ]
        }
        """;
    }

    private static string EscapeXml(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "items";
        }

        var chars = new List<char>(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current) && i > 0)
            {
                chars.Add('-');
            }

            chars.Add(char.ToLowerInvariant(current));
        }

        return new string(chars.ToArray());
    }
}
