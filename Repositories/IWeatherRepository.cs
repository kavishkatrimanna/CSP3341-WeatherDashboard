using WeatherDashboard.Models;

namespace WeatherDashboard.Repositories;

public interface IWeatherRepository
{
    Task<List<Location>> GetTrackedLocationsAsync();
    Task AddTrackedLocationAsync(Location location);
    Task RemoveTrackedLocationAsync(string locationId);

    Task SaveForecastAsync(Location location, List<ForecastDay> forecast);
    Task<List<ForecastDay>> GetCachedForecastAsync(string locationId);

    Task ClearCacheAsync();
}
