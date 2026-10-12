using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeatherDashboard.Models;
using WeatherDashboard.Repositories;
using WeatherDashboard.Services;

namespace WeatherDashboard.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IWeatherApiService _weatherApi;
    private readonly IWeatherRepository _repository;

    // Records whether another location was selected while loading
    private bool _reloadRequested;

    [ObservableProperty]
    private Location? _selectedLocation;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private User? _currentUser = new()
    {
        Username = "admin",
        Role = UserRole.Admin
    };

    public ObservableCollection<Location> Locations { get; } = new();

    public ObservableCollection<ForecastDay> ForecastDays { get; } = new();

    public bool IsAdmin =>
        CurrentUser?.IsInRole(UserRole.Admin) == true;

    public MainWindowViewModel(
        IWeatherApiService weatherApi,
        IWeatherRepository repository)
    {
        _weatherApi = weatherApi;
        _repository = repository;

        _ = InitializeAsync();
    }

    // Parameterless constructor for the Avalonia XAML previewer.
    public MainWindowViewModel()
        : this(
            new WeatherApiService(),
            new WeatherRepository("design_preview.db"))
    {
    }

    private async Task InitializeAsync()
    {
        try
        {
            await LoadLocationsAsync();

            if (Locations.Count > 0)
            {
                SelectedLocation = Locations[0];
            }
            else
            {
                ErrorMessage = "No weather locations are available.";
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[MainWindowViewModel] Initialization failed:\n{ex}");

            ErrorMessage = "Could not load weather locations.";
        }
    }

    public async Task LoadLocationsAsync()
    {
        var locations = await _repository.GetTrackedLocationsAsync();

        Locations.Clear();

        foreach (var location in locations)
        {
            Locations.Add(location);

            Console.WriteLine(
                $"[MainWindowViewModel] Location added: " +
                $"{location.Name}, {location.State} " +
                $"(ID: {location.LocationId})");
        }

        Console.WriteLine(
            $"[MainWindowViewModel] Total locations in dropdown: {Locations.Count}");

        Console.WriteLine(
            $"[MainWindowViewModel] Loaded {Locations.Count} locations.");
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadForecastAsync();
    }

    private async Task LoadForecastAsync()
    {
        if (SelectedLocation is null)
        {
            Console.WriteLine(
                "[MainWindowViewModel] No location selected.");

            return;
        }

        // If another request is running, remember that we need
        // to load the latest selected location afterward.
        if (IsBusy)
        {
            _reloadRequested = true;

            Console.WriteLine(
                "[MainWindowViewModel] Forecast request in progress. " +
                "The latest location will be loaded afterward.");

            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            do
            {
                _reloadRequested = false;

                var location = SelectedLocation;

                if (location is null)
                {
                    break;
                }

                Console.WriteLine(
                    $"[MainWindowViewModel] Loading forecast for {location.Name}.");

                var forecast = await _weatherApi.GetForecastAsync(
                    location.BomProductId);

                // Ignore this result if the user selected another
                // location while the API request was running.
                if (_reloadRequested ||
                    SelectedLocation?.LocationId != location.LocationId)
                {
                    continue;
                }

                Console.WriteLine(
                    $"[MainWindowViewModel] API returned {forecast.Count} forecasts.");

                await _repository.SaveForecastAsync(location, forecast);

                // The selection might have changed during the database save.
                if (_reloadRequested ||
                    SelectedLocation?.LocationId != location.LocationId)
                {
                    continue;
                }

                ForecastDays.Clear();

                foreach (var day in forecast)
                {
                    ForecastDays.Add(day);
                }

                Console.WriteLine(
                    $"[MainWindowViewModel] Display collection contains " +
                    $"{ForecastDays.Count} forecasts.");
            }
            while (_reloadRequested);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[MainWindowViewModel] Forecast loading failed:\n{ex}");

            ErrorMessage =
                "Could not refresh from BOM. Showing last cached forecast.";

            await LoadCachedForecastsAsync();
        }
        finally
        {
            IsBusy = false;

            // Handle a location change that occurred near the end
            // of the previous request.
            if (_reloadRequested)
            {
                _reloadRequested = false;
                _ = LoadForecastAsync();
            }
        }
    }

    private async Task LoadCachedForecastsAsync()
    {
        if (SelectedLocation is null)
        {
            return;
        }

        try
        {
            var cached = await _repository.GetCachedForecastAsync(
                SelectedLocation.LocationId);

            Console.WriteLine(
                $"[MainWindowViewModel] Retrieved {cached.Count} cached forecasts.");

            ForecastDays.Clear();

            foreach (var day in cached)
            {
                ForecastDays.Add(day);
            }

            if (cached.Count == 0)
            {
                Console.WriteLine(
                    "[MainWindowViewModel] No cached forecasts are available.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[MainWindowViewModel] Loading cached forecasts failed:\n{ex}");

            ErrorMessage =
                "Unable to load live or cached weather forecasts.";
        }
    }

    partial void OnSelectedLocationChanged(Location? value)
    {
        if (value is not null)
        {
            _ = LoadForecastAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(IsAdmin))]
    private async Task ClearCacheAsync()
    {
        try
        {
            await _repository.ClearCacheAsync();

            ForecastDays.Clear();
            ErrorMessage = null;

            Console.WriteLine(
                "[MainWindowViewModel] Cached weather data cleared.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[MainWindowViewModel] Clearing cache failed:\n{ex}");

            ErrorMessage = "Could not clear cached data.";
        }
    }
}