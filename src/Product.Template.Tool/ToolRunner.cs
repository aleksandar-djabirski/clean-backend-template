using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Product.Template.Tool;

public static class ToolRunner
{
    private const string AnalyzerPackageId = "CleanBackend.Guardrails.Analyzers";
    private const string AnalyzerPackageVersion = "0.1.0";

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
        var analyzerProject = FindTemplateRoot() is { } templateRoot
            ? Path.Combine(templateRoot, "src", "Product.Guardrails.Analyzers", "Product.Guardrails.Analyzers.csproj")
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

        // Pack the guardrail analyzer into a product-local NuGet feed so the generated product
        // references it by PackageReference and stays buildable off this machine.
        var hasAnalyzerPackage = false;
        if (analyzerProject is not null && File.Exists(analyzerProject))
        {
            var localFeed = Path.Combine(productDirectory, "eng", "local-feed");
            Directory.CreateDirectory(localFeed);
            using var packError = new StringWriter();
            var packExit = await RunProcessAsync(
                FindDotnet(),
                $"pack \"{analyzerProject}\" -c Release -o \"{localFeed}\" --nologo",
                Path.GetDirectoryName(analyzerProject)!,
                output,
                packError);

            if (packExit != 0)
            {
                throw new InvalidOperationException($"Failed to pack guardrail analyzer.{Environment.NewLine}{packError}");
            }

            hasAnalyzerPackage = true;
            await File.WriteAllTextAsync(Path.Combine(productDirectory, "nuget.config"), NuGetConfigFile());
            await File.WriteAllTextAsync(Path.Combine(productDirectory, ".editorconfig"), EditorConfigFile());
        }

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
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "AGENTS.md"), AgentsFile(productName, moduleName));
        await File.WriteAllTextAsync(Path.Combine(abstractionsDirectory, $"{productName}.Abstractions.csproj"), AbstractionsProjectFile());
        await File.WriteAllTextAsync(Path.Combine(abstractionsDirectory, "Error.cs"), ErrorFile(productName));
        await File.WriteAllTextAsync(Path.Combine(abstractionsDirectory, "Result.cs"), ResultFile(productName));
        await File.WriteAllTextAsync(Path.Combine(abstractionsDirectory, "Validation.cs"), ValidationFile(productName));
        await File.WriteAllTextAsync(Path.Combine(apiDirectory, $"{productName}.Api.csproj"), ApiProjectFile(productName, hasAnalyzerPackage));
        await File.WriteAllTextAsync(Path.Combine(apiDirectory, "Program.cs"), ProgramFile(productName, moduleName));
        await File.WriteAllTextAsync(Path.Combine(apiDirectory, "Guardrails", "EndpointAdapterAttribute.cs"), EndpointAdapterAttributeFile(productName));
        await File.WriteAllTextAsync(Path.Combine(apiDirectory, "Guardrails", "EndpointResults.cs"), EndpointResultsFile(productName));
        await File.WriteAllTextAsync(Path.Combine(moduleDirectory, $"{moduleName}Module.cs"), ModuleFile(productName, moduleName));
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "ownership-map.md"), OwnershipMapFile(productName, moduleName));
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "modules", $"{moduleName}.capabilities.md"), ModuleCapabilityMapFile(moduleName));
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "guardrail-exceptions.md"), GuardrailExceptionsDocFile());
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "migration-safety-profile.md"), MigrationSafetyProfileFile());
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "docs", "observability-baseline.md"), ObservabilityBaselineFile());
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "eng", "guardrail-exceptions.json"), GuardrailExceptionsConfigFile());
        await File.WriteAllTextAsync(Path.Combine(productDirectory, "eng", "package-policy.json"), PackagePolicyConfigFile());

        // Produce the committed lock file so locked restore is enforceable from the first commit.
        if (hasAnalyzerPackage)
        {
            var apiProject = Path.Combine(apiDirectory, $"{productName}.Api.csproj");
            using var restoreError = new StringWriter();
            var restoreExit = await RunProcessAsync(
                FindDotnet(),
                $"restore \"{apiProject}\"",
                productDirectory,
                output,
                restoreError);

            if (restoreExit != 0)
            {
                throw new InvalidOperationException($"Failed to generate lock file via restore.{Environment.NewLine}{restoreError}");
            }
        }

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

        var plannedFiles = kind switch
        {
            "command" => new Dictionary<string, string>
            {
                [Path.Combine(featureDirectory, $"{featureName}Command.cs")] = CommandFile(productName, moduleName, featureName),
                [Path.Combine(featureDirectory, $"{featureName}CommandHandler.cs")] = CommandHandlerFile(productName, moduleName, featureName),
                [Path.Combine(featureDirectory, $"{featureName}Endpoint.cs")] = CommandEndpointFile(productName, moduleName, featureName)
            },
            "query" => new Dictionary<string, string>
            {
                [Path.Combine(featureDirectory, $"{featureName}Query.cs")] = QueryFile(productName, moduleName, featureName),
                [Path.Combine(featureDirectory, $"{featureName}QueryHandler.cs")] = QueryHandlerFile(productName, moduleName, featureName),
                [Path.Combine(featureDirectory, $"{featureName}Endpoint.cs")] = QueryEndpointFile(productName, moduleName, featureName)
            },
            "weather-query" => new Dictionary<string, string>
            {
                [Path.Combine(featureDirectory, $"{featureName}Query.cs")] = WeatherQueryFile(productName, moduleName, featureName),
                [Path.Combine(featureDirectory, $"{featureName}QueryHandler.cs")] = WeatherQueryHandlerFile(productName, moduleName, featureName),
                [Path.Combine(featureDirectory, $"{featureName}Endpoint.cs")] = WeatherQueryEndpointFile(productName, moduleName, featureName)
            },
            _ => throw new InvalidOperationException("--kind must be 'command', 'query', or 'weather-query'.")
        };

        var existingFiles = plannedFiles.Keys.Where(File.Exists).ToArray();
        if (existingFiles.Length > 0)
        {
            throw new InvalidOperationException($"new-feature would overwrite existing files: {string.Join(", ", existingFiles)}");
        }

        Directory.CreateDirectory(featureDirectory);
        foreach (var plannedFile in plannedFiles)
        {
            await File.WriteAllTextAsync(plannedFile.Key, plannedFile.Value);
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

        var lockedRestore = ReadPackagePolicy(productDirectory)?.LockedRestoreRequired == true;
        var buildArguments = lockedRestore
            ? $"build \"{apiProject}\" -p:RestoreLockedMode=true"
            : $"build \"{apiProject}\"";

        var dotnet = FindDotnet();
        return await RunProcessAsync(dotnet, buildArguments, productDirectory, output, error);
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
        if (!projectText.Contains($"Include=\"{AnalyzerPackageId}\"", StringComparison.Ordinal))
        {
            problems.Add($"API project must reference the {AnalyzerPackageId} analyzer package.");
        }

        if (!projectText.Contains($"{productName}.Abstractions.csproj", StringComparison.Ordinal))
        {
            problems.Add($"API project must reference {productName}.Abstractions.");
        }

        problems.AddRange(VerifyRequiredFiles(productDirectory, productName));
        problems.AddRange(VerifyModuleBoundaries(productDirectory, productName));
        problems.AddRange(VerifyPackagePolicy(productDirectory, productName));
        problems.AddRange(VerifyGuardrailExceptions(productDirectory));

        var unresolved = Directory.EnumerateFiles(productDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}local-feed{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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
            Path.Combine(productDirectory, "AGENTS.md"),
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

    // Lexical boundary gate. In the single-assembly design there is no compiler-enforced module
    // boundary, so this complements the analyzers by asserting that feature files declare only
    // internal types (the module facade is the one public surface). It strips comments and string
    // literals first so it matches real declarations, not the word "public" in prose, and it is
    // independent of modifier order and covers every top-level type kind.
    private static readonly Regex PublicTypeDeclaration = new(
        @"(?<modifiers>(?:\b(?:public|internal|sealed|static|abstract|partial|file|readonly|ref)\b\s+)*)\b(?:class|struct|interface|enum|record|delegate)\b",
        RegexOptions.Compiled);

    private static IEnumerable<string> VerifyModuleBoundaries(string productDirectory, string productName)
    {
        var modulesDirectory = Path.Combine(productDirectory, "src", $"{productName}.Api", "Modules");
        if (!Directory.Exists(modulesDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(modulesDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.EndsWith("Module.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var code = StripCommentsAndStrings(File.ReadAllText(file));
            var declaresPublicType = PublicTypeDeclaration.Matches(code)
                .Any(match => Regex.IsMatch(match.Groups["modifiers"].Value, @"\bpublic\b"));

            if (declaresPublicType)
            {
                yield return $"Module implementation types must be internal by default: {file}";
            }
        }
    }

    private static string StripCommentsAndStrings(string source)
    {
        var builder = new System.Text.StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    i++;
                }

                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                var quote = c;
                var verbatim = i > 0 && source[i - 1] == '@';
                i++;
                while (i < source.Length)
                {
                    if (!verbatim && source[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (source[i] == quote)
                    {
                        if (verbatim && i + 1 < source.Length && source[i + 1] == quote)
                        {
                            i += 2;
                            continue;
                        }

                        break;
                    }

                    i++;
                }

                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private sealed record PackagePolicy(bool LockedRestoreRequired, IReadOnlyList<string> ProhibitedPackages);

    private static PackagePolicy? ReadPackagePolicy(string productDirectory)
    {
        var path = Path.Combine(productDirectory, "eng", "package-policy.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var locked = root.TryGetProperty("lockedRestoreRequired", out var lockedElement) &&
                     lockedElement.ValueKind == JsonValueKind.True;

        var prohibited = root.TryGetProperty("prohibitedPackages", out var prohibitedElement) &&
                         prohibitedElement.ValueKind == JsonValueKind.Array
            ? prohibitedElement.EnumerateArray()
                .Select(element => element.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray()
            : Array.Empty<string>();

        return new PackagePolicy(locked, prohibited);
    }

    private static IEnumerable<string> VerifyPackagePolicy(string productDirectory, string productName)
    {
        PackagePolicy? policy = null;
        string? parseError = null;
        try
        {
            policy = ReadPackagePolicy(productDirectory);
        }
        catch (JsonException exception)
        {
            parseError = exception.Message;
        }

        if (parseError is not null)
        {
            yield return $"package-policy.json is not valid JSON: {parseError}";
            yield break;
        }

        if (policy is null)
        {
            yield break;
        }

        var sourceDirectory = Path.Combine(productDirectory, "src");
        var projectFiles = Directory.Exists(sourceDirectory)
            ? Directory.EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories).ToArray()
            : Array.Empty<string>();

        foreach (var project in projectFiles)
        {
            var projectText = File.ReadAllText(project);
            foreach (var prohibited in policy.ProhibitedPackages)
            {
                if (projectText.Contains($"Include=\"{prohibited}\"", StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"Prohibited package '{prohibited}' is referenced in {project}.";
                }
            }
        }

        if (!policy.LockedRestoreRequired)
        {
            yield break;
        }

        var apiDirectory = Path.Combine(sourceDirectory, $"{productName}.Api");
        var lockFile = Path.Combine(apiDirectory, "packages.lock.json");
        if (!File.Exists(lockFile))
        {
            yield return $"package-policy.json requires locked restore but packages.lock.json is missing: {lockFile}";
        }

        var apiProject = Path.Combine(apiDirectory, $"{productName}.Api.csproj");
        if (File.Exists(apiProject) &&
            !File.ReadAllText(apiProject).Contains("RestorePackagesWithLockFile", StringComparison.Ordinal))
        {
            yield return $"package-policy.json requires locked restore but {productName}.Api.csproj does not set RestorePackagesWithLockFile.";
        }
    }

    private static IEnumerable<string> VerifyGuardrailExceptions(string productDirectory)
    {
        var path = Path.Combine(productDirectory, "eng", "guardrail-exceptions.json");
        if (!File.Exists(path))
        {
            yield break;
        }

        JsonElement root = default;
        string? parseError = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            parseError = exception.Message;
        }

        if (parseError is not null)
        {
            yield return $"guardrail-exceptions.json is not valid JSON: {parseError}";
            yield break;
        }

        if (!root.TryGetProperty("exceptions", out var exceptions) || exceptions.ValueKind != JsonValueKind.Array)
        {
            yield return "guardrail-exceptions.json must contain an 'exceptions' array.";
            yield break;
        }

        var index = 0;
        foreach (var exception in exceptions.EnumerateArray())
        {
            var label = $"guardrail exception #{index}";
            index++;

            foreach (var field in new[] { "rule", "module", "reason" })
            {
                if (!exception.TryGetProperty(field, out var value) ||
                    value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(value.GetString()))
                {
                    yield return $"{label} is missing required string field '{field}'.";
                }
            }

            if (!exception.TryGetProperty("tests", out var tests) ||
                tests.ValueKind != JsonValueKind.Array ||
                !tests.EnumerateArray().Any())
            {
                yield return $"{label} must list at least one proving test in 'tests'.";
            }

            if (!exception.TryGetProperty("expires", out var expires) ||
                expires.ValueKind != JsonValueKind.String ||
                !DateOnly.TryParse(expires.GetString(), out var expiry))
            {
                yield return $"{label} must set an 'expires' date (YYYY-MM-DD).";
            }
            else if (expiry < DateOnly.FromDateTime(DateTime.UtcNow.Date))
            {
                yield return $"{label} expired on {expiry:yyyy-MM-dd}; renew the evidence or remove the exception.";
            }
        }
    }

    private static async Task UpdateModuleFileAsync(string apiDirectory, string productName, string moduleName)
    {
        var moduleDirectory = Path.Combine(apiDirectory, "Modules", moduleName);
        var moduleFile = Path.Combine(moduleDirectory, $"{moduleName}Module.cs");
        var features = Directory.GetDirectories(moduleDirectory)
            .Select(directory => Path.GetFileName(directory)!)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new FeatureRegistration(name, ResolveHandlerType(Path.Combine(moduleDirectory, name), name)))
            .Where(feature => feature.HandlerType is not null)
            .ToArray();

        await File.WriteAllTextAsync(moduleFile, ModuleFile(productName, moduleName, features));
    }

    private static string? ResolveHandlerType(string featureDirectory, string featureName)
    {
        // Determine command-vs-query from the generated handler file that actually exists,
        // not from the feature name. The scaffold kind is authoritative.
        if (File.Exists(Path.Combine(featureDirectory, $"{featureName}CommandHandler.cs")))
        {
            return $"{featureName}CommandHandler";
        }

        if (File.Exists(Path.Combine(featureDirectory, $"{featureName}QueryHandler.cs")))
        {
            return $"{featureName}QueryHandler";
        }

        return null;
    }

    private readonly record struct FeatureRegistration(string Name, string? HandlerType);

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
        output.WriteLine("  bootstrap --name SampleProduct --module Requests --output /tmp (legacy; dotnet new clean-backend is preferred)");
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

    private static string ApiProjectFile(string productName, bool hasAnalyzerPackage)
    {
        var analyzerReference = hasAnalyzerPackage
            ? $"""
                <PackageReference Include="{AnalyzerPackageId}" Version="{AnalyzerPackageVersion}" PrivateAssets="all" />
            """
            : string.Empty;

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <WarningsAsErrors>$(WarningsAsErrors);PGB001;PGB006</WarningsAsErrors>
            <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1900</WarningsNotAsErrors>
            <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="../{{productName}}.Abstractions/{{productName}}.Abstractions.csproj" />
        {{analyzerReference}}
          </ItemGroup>
        </Project>
        """;
    }

    private static string NuGetConfigFile()
    {
        // Generated products restore the guardrail analyzer from a committed product-local feed,
        // so the product builds without depending on this template repo or its absolute paths.
        return """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="guardrails-local" value="eng/local-feed" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
        </configuration>
        """;
    }

    private static string EditorConfigFile()
    {
        // PGB003 remains at analyzer-default Info severity. It is a heuristic spike and must stay
        // visible without becoming build-breaking under TreatWarningsAsErrors.
        return """
        root = true

        [*.cs]
        # PGB003 is intentionally not promoted here.
        """;
    }

    private static string ErrorFile(string productName)
    {
        return $$"""
        namespace {{productName}}.Abstractions;

        public enum ErrorType
        {
            Failure,
            Validation,
            NotFound,
            Conflict
        }

        public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
        {
            public static Error None { get; } = new(string.Empty, string.Empty);

            public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

            public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

            public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

            public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
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

        // Validators are synchronous by design: validation is pure, in-memory request shape
        // checking. Anything that needs I/O is a business rule for a handler, not a validator.
        public interface IValidator<in TRequest>
        {
            IReadOnlyList<Error> Validate(TRequest request);
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
        builder.Services.AddSingleton(TimeProvider.System);
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

    private static string EndpointResultsFile(string productName)
    {
        // The single place where Result/Error becomes HTTP. Handlers stay HTTP-free and return
        // Result<T>; endpoint adapters translate here. Failures become RFC 7807 ProblemDetails with
        // a status derived from the error type, so the shipped abstractions are actually used.
        return $$"""
        using {{productName}}.Abstractions;
        using Microsoft.AspNetCore.Http;

        namespace {{productName}}.Api.Guardrails;

        internal static class EndpointResults
        {
            public static IResult Ok<TValue>(Result<TValue> result) =>
                result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);

            public static IResult Created<TValue>(Result<TValue> result, Func<TValue, string> location) =>
                result.IsSuccess ? Results.Created(location(result.Value), result.Value) : Problem(result.Error);

            public static IResult Problem(Error error)
            {
                var statusCode = error.Type switch
                {
                    ErrorType.Validation => StatusCodes.Status400BadRequest,
                    ErrorType.NotFound => StatusCodes.Status404NotFound,
                    ErrorType.Conflict => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status500InternalServerError
                };

                return Results.Problem(
                    title: error.Code,
                    detail: error.Message,
                    statusCode: statusCode);
            }
        }
        """;
    }

    private static string ModuleFile(string productName, string moduleName, params FeatureRegistration[] features)
    {
        var usings = string.Join(Environment.NewLine, features.Select(feature => $"using {productName}.Api.Modules.{moduleName}.{feature.Name};"));
        var serviceRegistrations = string.Join(Environment.NewLine, features.Select(feature => $"        services.AddScoped<{feature.HandlerType}>();"));
        var endpointMappings = string.Join(Environment.NewLine, features.Select(feature => $"        endpoints.Map{feature.Name}Endpoint();"));

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
        using {{productName}}.Abstractions;

        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        internal sealed class {{featureName}}CommandHandler
        {
            public Task<Result<Guid>> HandleAsync({{featureName}}Command command, CancellationToken cancellationToken)
            {
                return Task.FromResult(Result.Success(Guid.NewGuid()));
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
                var result = await handler.HandleAsync(command, cancellationToken);
                return EndpointResults.Created(result, id => $"/{{route}}/{id}");
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
        using {{productName}}.Abstractions;

        namespace {{productName}}.Api.Modules.{{moduleName}}.{{featureName}};

        internal sealed class {{featureName}}QueryHandler
        {
            public Task<Result<{{featureName}}Response>> HandleAsync({{featureName}}Query query, CancellationToken cancellationToken)
            {
                return Task.FromResult(Result.Success(new {{featureName}}Response(query.Id, "sample")));
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
                var result = await handler.HandleAsync(new {{featureName}}Query(id), cancellationToken);
                return EndpointResults.Ok(result);
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
        using {{productName}}.Abstractions;

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

            private readonly TimeProvider _timeProvider;

            public {{featureName}}QueryHandler(TimeProvider timeProvider)
            {
                _timeProvider = timeProvider;
            }

            public Task<Result<IReadOnlyList<WeatherForecast>>> HandleAsync({{featureName}}Query query, CancellationToken cancellationToken)
            {
                // Time comes from the injected TimeProvider, never the ambient system clock, and the
                // sample values are derived deterministically rather than from ambient randomness.
                var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
                var forecasts = Enumerable.Range(1, 5)
                    .Select(index => new WeatherForecast(
                        today.AddDays(index),
                        -20 + (index * 13 % 75),
                        Summaries[index % Summaries.Length]))
                    .ToArray();

                return Task.FromResult(Result.Success<IReadOnlyList<WeatherForecast>>(forecasts));
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
                var result = await handler.HandleAsync(new {{featureName}}Query(), cancellationToken);
                return EndpointResults.Ok(result);
            }
        }
        """;
    }

    private static string AgentsFile(string productName, string moduleName)
    {
        return $$"""
        # AGENTS.md

        Daily contract for anyone (human or AI) changing {{productName}}. Read this before writing code.

        ## Architecture

        - Modular monolith inside a single `{{productName}}.Api` assembly. Modules are folders under
          `src/{{productName}}.Api/Modules/<Module>`, each a vertical slice of features.
        - This is a deliberate single-assembly choice. Because there are no compiler-enforced
          assembly boundaries, the guardrail analyzers and `verify` are the boundary enforcement.
          Do not weaken them to take a shortcut.
        - `{{productName}}.Abstractions` holds shared `Result`, `Error`, and `IValidator`. It must not
          depend on the API project.

        ## Module rules

        - Only the `<Module>Module.cs` facade is public. Every feature type (command, query, handler,
          endpoint, record) is `internal`. `verify` fails if a non-facade module type is public.
        - A feature is a folder with a command/query, its handler, and an endpoint adapter.
        - Scaffold features with the tool, never by hand-copying:
          - `new-feature --product . --module {{moduleName}} --kind command --name CreateThing`
          - `new-feature --product . --module {{moduleName}} --kind query --name GetThing`

        ## Guardrails (enforced by analyzers, build-breaking)

        - **PGB001** Query handlers must not mutate EF Core state: no `Add/Update/Remove/SaveChanges`,
          no `ExecuteUpdate/ExecuteDelete/ExecuteSql*`, no `Entry(x).State = ...`. Writes belong in
          command handlers.
        - **PGB006** Route mapping (`MapGet/MapPost/...`) must live inside a type/method marked
          `[EndpointAdapter]`, and an endpoint may inject only bound request data, a
          `CancellationToken`, and one typed command/query handler. No `HttpContext`, `IConfiguration`,
          `DbContext`, `ClaimsPrincipal`, `IMediator`, etc.
        - **PGB003** Heuristically reports repository/CRUD/unit-of-work persistence wrappers (generic or hand-written).
          It is intentionally visible but non-breaking by default until real-product evidence proves whether to promote, narrow, or retire it.

        ## Conventions

        - Get time from the injected `TimeProvider`, never `DateTime.UtcNow`/`Now`. Avoid `Random.Shared`
          and other ambient statics in handlers.
        - Validators (`IValidator<T>`) are synchronous and pure. I/O is a handler concern.
        - Return `Result`/`Result<T>` from handlers and translate at the endpoint edge.

        ## Packages

        - `eng/package-policy.json` is enforced by `verify`: prohibited packages fail the build and
          locked restore (`packages.lock.json`) is required. Commit the lock file.
        - New provider SDKs, brokers, caches, observability exporters, and background-job libraries
          require an explicit decision record before they are added.

        ## Guardrail exceptions

        - Exceptions are rare, local, and temporary. Record them in `eng/guardrail-exceptions.json`
          with `rule`, `module`, `reason`, `tests`, and an `expires` date. `verify` fails on an
          expired or incomplete exception. Never weaken a rule globally.

        ## Before you say it works

        - Run `verify --product .` (structure checks + analyzer-enabled build + locked restore).
        - Verification output is the evidence. Do not claim green without it.
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

        Required evidence (enforced by `verify` against `eng/guardrail-exceptions.json`):

        - `rule`: rule id or protected asset.
        - `module`: owning module.
        - `reason`: why the normal shape cannot work.
        - `tests`: at least one test proving the exception remains safe.
        - `expires`: review/expiry date (`YYYY-MM-DD`). `verify` fails once this date passes.

        Example entry:

        ```json
        {
          "exceptions": [
            {
              "rule": "PGB003",
              "module": "Billing",
              "reason": "Third-party SDK requires a thin persistence wrapper.",
              "tests": ["BillingRepositoryBoundaryTests"],
              "expires": "2026-12-31"
            }
          ]
        }
        ```

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
