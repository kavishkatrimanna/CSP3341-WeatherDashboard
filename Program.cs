using Avalonia;

namespace WeatherDashboard;

internal static class Program
{
    // Avalonia's bootstrap entry point. This is plain, synchronous, top-level
    // start-up code -- the UI framework itself owns the message loop from here.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
