using Product.Api.Modules.Weather.GetWeatherForecast;

namespace Product.Api.Modules.Weather;

internal static class WeatherModule
{
    public static IServiceCollection AddWeatherModule(this IServiceCollection services)
    {
        services.AddScoped<GetWeatherForecastQueryHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapWeatherEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGetWeatherForecastEndpoint();
        return endpoints;
    }
}
