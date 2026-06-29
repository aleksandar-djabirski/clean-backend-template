using Product.Template.Tool;

var root = Path.Combine(Path.GetTempPath(), "product-template-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await RunTool("bootstrap", "--name", "SampleProduct", "--module", "Requests", "--output", root);

    var product = Path.Combine(root, "SampleProduct");
    await RunTool("new-feature", "--product", product, "--module", "Requests", "--kind", "command", "--name", "CreateRequest");
    await RunTool("new-feature", "--product", product, "--module", "Requests", "--kind", "query", "--name", "GetRequest");
    await RunTool("new-feature", "--product", product, "--module", "Requests", "--kind", "weather-query", "--name", "GetWeatherForecast");
    await RunTool("verify", "--product", product);

    AssertFile(Path.Combine(product, "global.json"));
    AssertFile(Path.Combine(product, "SampleProduct.slnx"));
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
    var weatherEndpoint = Path.Combine(product, "src", "SampleProduct.Api", "Modules", "Requests", "GetWeatherForecast", "GetWeatherForecastEndpoint.cs");
    var weatherHandler = Path.Combine(product, "src", "SampleProduct.Api", "Modules", "Requests", "GetWeatherForecast", "GetWeatherForecastQueryHandler.cs");
    AssertFile(weatherEndpoint);
    AssertFile(weatherHandler);
    AssertContains(weatherEndpoint, "endpoints.MapGet(\"/weatherforecast\", HandleAsync);");
    AssertContains(weatherHandler, "private static readonly string[] Summaries");
    Console.WriteLine("PASS generated product bootstraps Phase 1 baseline, scaffolds command/query/weather-query, builds, and verifies");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
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
