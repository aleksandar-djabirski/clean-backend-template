using Product.Api.Modules.Weather;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddWeatherModule();

var app = builder.Build();
app.MapWeatherEndpoints();
app.Run();

public partial class Program;
