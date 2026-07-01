using Product.Abstractions;

namespace Product.Api.Modules.Weather.GetWeatherForecast;

internal sealed class GetWeatherForecastQueryHandler
{
    private static readonly string[] Summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild"];
    private readonly TimeProvider _timeProvider;

    public GetWeatherForecastQueryHandler(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public Task<Result<IReadOnlyList<WeatherForecast>>> HandleAsync(GetWeatherForecastQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var forecasts = Enumerable.Range(1, 5)
            .Select(index => new WeatherForecast(today.AddDays(index), -20 + (index * 13 % 75), Summaries[index % Summaries.Length]))
            .ToArray();
        return Task.FromResult(Result.Success<IReadOnlyList<WeatherForecast>>(forecasts));
    }
}
