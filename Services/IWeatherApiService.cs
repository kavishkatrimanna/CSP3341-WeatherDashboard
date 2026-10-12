using WeatherDashboard.Models;

namespace WeatherDashboard.Services;

/// <summary>
/// Abstraction over the external weather data source. MainWindowViewModel
/// depends on this interface, not on WeatherApiService directly, so a fake
/// implementation can be substituted in unit tests without any network access.
/// </summary>
public interface IWeatherApiService
{
    Task<List<ForecastDay>> GetForecastAsync(string bomProductId, CancellationToken ct = default);
}
