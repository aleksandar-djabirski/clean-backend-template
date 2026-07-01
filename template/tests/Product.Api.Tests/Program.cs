using Microsoft.AspNetCore.Mvc.Testing;

await using var application = new WebApplicationFactory<Program>();
using var client = application.CreateClient();
using var response = await client.GetAsync("/weatherforecast");
response.EnsureSuccessStatusCode();
var body = await response.Content.ReadAsStringAsync();
if (!body.Contains("temperatureC", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Weather endpoint did not return the expected result-mapped response.");
}
Console.WriteLine("PASS weather endpoint returns a result-mapped response");
