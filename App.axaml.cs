using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WeatherDashboard.Repositories;
using WeatherDashboard.Services;
using WeatherDashboard.ViewModels;
using WeatherDashboard.Views;

namespace WeatherDashboard;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Manual "composition root": this app is small enough that a full DI
            // container (Microsoft.Extensions.DependencyInjection) would be overkill.
            // Each concrete service is wired to its interface here, once, at start-up.
            IWeatherApiService weatherApi = new WeatherApiService();
            IWeatherRepository repository = new WeatherRepository("weather_cache.db");

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(weatherApi, repository)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
