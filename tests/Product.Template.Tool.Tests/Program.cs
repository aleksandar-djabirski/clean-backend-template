using Product.Template.Tool;

var root = Path.Combine(Path.GetTempPath(), "product-template-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await RunBaselineCommandQuerySmoke(root);
    await RunKindRegistrationSmoke(root);
    await RunPackagePolicySmoke(root);
    await RunPhase3CandidateWeatherQuerySmoke(root);
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task RunBaselineCommandQuerySmoke(string root)
{
    await RunTool("bootstrap", "--name", "SampleProduct", "--module", "Requests", "--output", root);

    var product = Path.Combine(root, "SampleProduct");
    await RunTool("new-feature", "--product", product, "--module", "Requests", "--kind", "command", "--name", "CreateRequest");
    await RunTool("new-feature", "--product", product, "--module", "Requests", "--kind", "query", "--name", "GetRequest");
    await RunTool("verify", "--product", product);

    AssertFile(Path.Combine(product, "global.json"));
    AssertFile(Path.Combine(product, "SampleProduct.slnx"));
    AssertFile(Path.Combine(product, "AGENTS.md"));
    AssertFile(Path.Combine(product, "docs", "ownership-map.md"));
    AssertFile(Path.Combine(product, "docs", "modules", "Requests.capabilities.md"));
    AssertFile(Path.Combine(product, "docs", "guardrail-exceptions.md"));
    AssertFile(Path.Combine(product, "docs", "migration-safety-profile.md"));
    AssertFile(Path.Combine(product, "docs", "observability-baseline.md"));
    AssertFile(Path.Combine(product, "eng", "guardrail-exceptions.json"));
    AssertFile(Path.Combine(product, "eng", "package-policy.json"));
    AssertFile(Path.Combine(product, "src", "SampleProduct.Abstractions", "SampleProduct.Abstractions.csproj"));
    AssertFile(Path.Combine(product, "src", "SampleProduct.Api", "SampleProduct.Api.csproj"));
    AssertFile(Path.Combine(product, "src", "SampleProduct.Api", "Modules", "Requests", "CreateRequest", "CreateRequestEndpoint.cs"));
    AssertFile(Path.Combine(product, "src", "SampleProduct.Api", "Modules", "Requests", "GetRequest", "GetRequestEndpoint.cs"));

    // Portability: the analyzer must come from a committed product-local NuGet feed referenced
    // by PackageReference, never an absolute path into this template's bin output.
    AssertFile(Path.Combine(product, "nuget.config"));
    AssertFile(Path.Combine(product, ".editorconfig"));
    AssertFile(Path.Combine(product, "eng", "local-feed", "Product.Guardrails.Analyzers.0.1.0.nupkg"));
    var apiProject = Path.Combine(product, "src", "SampleProduct.Api", "SampleProduct.Api.csproj");
    AssertContains(apiProject, "<PackageReference Include=\"Product.Guardrails.Analyzers\"");
    AssertNotContains(apiProject, "<Analyzer Include=");
    AssertNotContains(apiProject, "bin");

    // Result/Error must actually be used: handlers return Result<T> and endpoints translate at the
    // edge through EndpointResults, never returning raw values via Results.Ok/Created directly.
    var modules = Path.Combine(product, "src", "SampleProduct.Api", "Modules", "Requests");
    AssertFile(Path.Combine(product, "src", "SampleProduct.Api", "Guardrails", "EndpointResults.cs"));
    AssertContains(Path.Combine(modules, "CreateRequest", "CreateRequestCommandHandler.cs"), "Task<Result<Guid>>");
    AssertContains(Path.Combine(modules, "CreateRequest", "CreateRequestEndpoint.cs"), "EndpointResults.Created(result");
    AssertContains(Path.Combine(modules, "GetRequest", "GetRequestQueryHandler.cs"), "Task<Result<GetRequestResponse>>");
    AssertContains(Path.Combine(modules, "GetRequest", "GetRequestEndpoint.cs"), "EndpointResults.Ok(result)");
    Console.WriteLine("PASS baseline command/query smoke: generated product bootstraps, scaffolds command/query, builds, verifies, and references the analyzer as a portable package");
}

static async Task RunKindRegistrationSmoke(string root)
{
    // Names are deliberately chosen to defeat any name-based command/query heuristic:
    // a query whose name implies a command, and a command whose name implies a query.
    await RunTool("bootstrap", "--name", "KindProduct", "--module", "Orders", "--output", root);

    var product = Path.Combine(root, "KindProduct");
    await RunTool("new-feature", "--product", product, "--module", "Orders", "--kind", "query", "--name", "SearchOrders");
    await RunTool("new-feature", "--product", product, "--module", "Orders", "--kind", "command", "--name", "GetReport");
    await RunTool("verify", "--product", product);

    var moduleFile = Path.Combine(product, "src", "KindProduct.Api", "Modules", "Orders", "OrdersModule.cs");
    AssertContains(moduleFile, "services.AddScoped<SearchOrdersQueryHandler>();");
    AssertContains(moduleFile, "services.AddScoped<GetReportCommandHandler>();");

    // Boundary gate: a public feature type (not the module facade) must fail verify. The word
    // "public" in a comment/string must not, proving the check inspects declarations, not text.
    var leak = Path.Combine(product, "src", "KindProduct.Api", "Modules", "Orders", "SearchOrders", "Leak.cs");
    await File.WriteAllTextAsync(leak, "namespace KindProduct.Api.Modules.Orders.SearchOrders;\n\npublic sealed record Leak(string Value);\n");
    if (await RunToolRaw("verify", "--product", product) == 0)
    {
        throw new InvalidOperationException("verify accepted a public module implementation type; boundary gate is not enforced.");
    }

    await File.WriteAllTextAsync(leak, "namespace KindProduct.Api.Modules.Orders.SearchOrders;\n\n// a public record would leak here\ninternal sealed record NotALeak(string Value);\n");
    if (await RunToolRaw("verify", "--product", product) != 0)
    {
        throw new InvalidOperationException("verify rejected an internal type whose comment merely mentions 'public'; boundary gate matches text, not declarations.");
    }

    File.Delete(leak);
    Console.WriteLine("PASS kind registration smoke: --kind drives registration, the product builds, and the boundary gate flags public types without false-positiving on prose");
}

static async Task RunPackagePolicySmoke(string root)
{
    await RunTool("bootstrap", "--name", "PolicyProduct", "--module", "Requests", "--output", root);

    var product = Path.Combine(root, "PolicyProduct");
    var apiProject = Path.Combine(product, "src", "PolicyProduct.Api", "PolicyProduct.Api.csproj");

    // Locked restore is a real control: the lock file is committed at bootstrap.
    AssertFile(Path.Combine(product, "src", "PolicyProduct.Api", "packages.lock.json"));
    await RunTool("verify", "--product", product);

    // Prohibited packages are a real control: referencing one must fail verify.
    var projectText = await File.ReadAllTextAsync(apiProject);
    var tampered = projectText.Replace(
        "</ItemGroup>",
        "  <PackageReference Include=\"Microsoft.EntityFrameworkCore.Proxies\" Version=\"10.0.0\" />\n  </ItemGroup>",
        StringComparison.Ordinal);
    await File.WriteAllTextAsync(apiProject, tampered);

    var exitCode = await RunToolRaw("verify", "--product", product);
    if (exitCode == 0)
    {
        throw new InvalidOperationException("verify accepted a prohibited package reference; package policy is not enforced.");
    }

    await File.WriteAllTextAsync(apiProject, projectText);

    // Guardrail exceptions are a real control: an expired/incomplete exception must fail verify.
    var exceptionsFile = Path.Combine(product, "eng", "guardrail-exceptions.json");
    await File.WriteAllTextAsync(exceptionsFile, "{ \"exceptions\": [ { \"rule\": \"PGB003\", \"module\": \"Requests\", \"reason\": \"stale\", \"tests\": [\"X\"], \"expires\": \"2000-01-01\" } ] }");

    var expiredExit = await RunToolRaw("verify", "--product", product);
    if (expiredExit == 0)
    {
        throw new InvalidOperationException("verify accepted an expired guardrail exception; the exception mechanism is not enforced.");
    }

    Console.WriteLine("PASS package policy smoke: lock file committed, prohibited packages fail verify, expired guardrail exceptions fail verify");
}

static async Task RunPhase3CandidateWeatherQuerySmoke(string root)
{
    await RunTool("bootstrap", "--name", "WeatherSampleProduct", "--module", "Weather", "--output", root);

    var product = Path.Combine(root, "WeatherSampleProduct");
    await RunTool("new-feature", "--product", product, "--module", "Weather", "--kind", "weather-query", "--name", "GetWeatherForecast");
    await RunTool("verify", "--product", product);

    var weatherEndpoint = Path.Combine(product, "src", "WeatherSampleProduct.Api", "Modules", "Weather", "GetWeatherForecast", "GetWeatherForecastEndpoint.cs");
    var weatherHandler = Path.Combine(product, "src", "WeatherSampleProduct.Api", "Modules", "Weather", "GetWeatherForecast", "GetWeatherForecastQueryHandler.cs");
    AssertFile(weatherEndpoint);
    AssertFile(weatherHandler);
    AssertContains(weatherEndpoint, "endpoints.MapGet(\"/weatherforecast\", HandleAsync);");
    AssertContains(weatherHandler, "private static readonly string[] Summaries");
    // The reference feature must obey the template's own TimeProvider/no-ambient-statics rule.
    AssertContains(weatherHandler, "TimeProvider");
    AssertNotContains(weatherHandler, "DateTime.UtcNow");
    AssertNotContains(weatherHandler, "Random.Shared");
    Console.WriteLine("PASS Phase 3 candidate weather-query smoke: mechanics verified; this does not prove Phase 3 admission");
}

static async Task RunTool(params string[] args)
{
    using var output = new StringWriter();
    using var error = new StringWriter();

    var exitCode = await ToolRunner.RunAsync(args, output, error);

    if (exitCode != 0)
    {
        throw new InvalidOperationException($"Tool failed with exit code {exitCode}.{Environment.NewLine}OUT:{Environment.NewLine}{output}{Environment.NewLine}ERR:{Environment.NewLine}{error}");
    }

    Console.Write(output.ToString());
}

static async Task<int> RunToolRaw(params string[] args)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    return await ToolRunner.RunAsync(args, output, error);
}

static void AssertFile(string path)
{
    if (!File.Exists(path))
    {
        throw new InvalidOperationException($"Expected file does not exist: {path}");
    }
}

static void AssertContains(string path, string expected)
{
    var text = File.ReadAllText(path);
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected file {path} to contain: {expected}");
    }
}

static void AssertNotContains(string path, string unexpected)
{
    var text = File.ReadAllText(path);
    if (text.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected file {path} to NOT contain: {unexpected}");
    }
}
