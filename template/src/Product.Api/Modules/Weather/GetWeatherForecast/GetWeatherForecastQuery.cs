namespace Product.Api.Modules.Weather.GetWeatherForecast;

internal sealed record GetWeatherForecastQuery;

internal sealed record WeatherForecast(DateOnly Date, int TemperatureC, string Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
