namespace WeatherDashboard.Models;

public class ForecastDay
{
    public DateTime ForecastDate { get; set; }

    public double MinTemp { get; set; }

    public double MaxTemp { get; set; }

    public double Humidity { get; set; }

    public double RainfallMm { get; set; }

    public double RainProbability { get; set; }

    public string DayLabel => ForecastDate.ToString("ddd");

    // Scale temperatures so the bars are easier to compare visually.
    public double MinTempBarHeight =>
        double.IsFinite(MinTemp)
            ? Math.Clamp(MinTemp * 4, 0, 180)
            : 0;

    public double MaxTempBarHeight =>
        double.IsFinite(MaxTemp)
            ? Math.Clamp(MaxTemp * 4, 0, 180)
            : 0;

    // Display unavailable measurements without showing NaN.
    public string HumidityLabel =>
        double.IsFinite(Humidity)
            ? $"{Humidity:0}%"
            : "N/A";

    public string RainProbabilityLabel =>
        double.IsFinite(RainProbability)
            ? $"{RainProbability:0}%"
            : "N/A";
}