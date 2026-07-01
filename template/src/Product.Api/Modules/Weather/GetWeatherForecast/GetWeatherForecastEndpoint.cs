using Product.Api.Guardrails;

namespace Product.Api.Modules.Weather.GetWeatherForecast;

[EndpointAdapter]
internal static class GetWeatherForecastEndpoint
{
    public static IEndpointRouteBuilder MapGetWeatherForecastEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/weatherforecast", HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(GetWeatherForecastQueryHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetWeatherForecastQuery(), cancellationToken);
        return EndpointResults.Ok(result);
    }
}
