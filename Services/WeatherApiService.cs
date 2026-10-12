using System.Globalization;
using System.Xml.Linq;
using WeatherDashboard.Models;

namespace WeatherDashboard.Services;

/// <summary>
/// Retrieves weather forecasts from the Australian Bureau of Meteorology (BOM).
/// Downloads XML feeds, parses forecast data, and returns daily forecast records.
/// Sample data is used as a fallback if the live feed cannot be processed.
/// </summary>
public class WeatherApiService : IWeatherApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public WeatherApiService(
        HttpClient? httpClient = null,
        string baseUrl = "http://www.bom.gov.au/fwo/")
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _baseUrl = baseUrl;
    }

    public async Task<List<ForecastDay>> GetForecastAsync(
        string bomProductId,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}{bomProductId}.xml";

            Console.WriteLine(
                $"[WeatherApiService] Requesting: {url}");

            using var response = await _httpClient.GetAsync(url, ct);

            Console.WriteLine(
                $"[WeatherApiService] HTTP status: " +
                $"{(int)response.StatusCode} {response.StatusCode}");

            response.EnsureSuccessStatusCode();

            var xml = await response.Content.ReadAsStringAsync(ct);

            Console.WriteLine(
                $"[WeatherApiService] Received {xml.Length} characters of XML.");

            var forecasts = ParseBomXml(xml);

            Console.WriteLine(
                $"[WeatherApiService] Valid daily forecasts: {forecasts.Count}");

            foreach (var forecast in forecasts.Take(5))
            {
                Console.WriteLine(
                    $"Date: {forecast.ForecastDate:yyyy-MM-dd}, " +
                    $"Min: {forecast.MinTemp}, " +
                    $"Max: {forecast.MaxTemp}, " +
                    $"Humidity: {FormatOptionalValue(forecast.Humidity, "%")}, " +
                    $"Rain probability: " +
                    $"{FormatOptionalValue(forecast.RainProbability, "%")}");
            }

            if (forecasts.Count > 0)
            {
                return forecasts;
            }

            Console.Error.WriteLine(
                "[WeatherApiService] No usable forecasts found. Using sample data.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Preserve cancellation requested by the caller.
            throw;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine(
                $"[WeatherApiService] HTTP request failed: {ex.Message}");
        }
        catch (OperationCanceledException ex)
        {
            Console.Error.WriteLine(
                $"[WeatherApiService] Request timed out: {ex.Message}");
        }
        catch (System.Xml.XmlException ex)
        {
            Console.Error.WriteLine(
                $"[WeatherApiService] Invalid XML response: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[WeatherApiService] Unexpected error: {ex.Message}");
        }

        return GenerateSampleForecast();
    }

    /// <summary>
    /// Parses the BOM XML and combines valid forecast periods into daily records.
    /// This implementation currently targets the Perth forecast area.
    /// Humidity is unavailable in the selected daily forecast periods.
    /// </summary>
    private static List<ForecastDay> ParseBomXml(string xml)
    {
        var result = new List<ForecastDay>();

        var document = XDocument.Parse(xml);

        var areas = document.Descendants("area").ToList();

        Console.WriteLine(
            $"[WeatherApiService] XML contains {areas.Count} areas.");

        var area = areas.FirstOrDefault(a =>
            string.Equals(
                ((string?)a.Attribute("description"))?.Trim(),
                "Perth",
                StringComparison.OrdinalIgnoreCase));

        if (area is null)
        {
            Console.Error.WriteLine(
                "[WeatherApiService] Could not find an area named Perth.");

            return result;
        }

        var periods = area.Elements("forecast-period");

        var dailyData = new Dictionary<DateOnly, List<ForecastDay>>();

        foreach (var period in periods)
        {
            var startTime = (string?)period.Attribute("start-time-local");

            if (!DateTime.TryParse(
                    startTime,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var forecastDate))
            {
                continue;
            }

            string? ReadElement(string type) =>
                period.Elements("element")
                    .FirstOrDefault(e =>
                        string.Equals(
                            (string?)e.Attribute("type"),
                            type,
                            StringComparison.OrdinalIgnoreCase))
                    ?.Value.Trim();

            string? ReadText(string type) =>
                period.Elements("text")
                    .FirstOrDefault(e =>
                        string.Equals(
                            (string?)e.Attribute("type"),
                            type,
                            StringComparison.OrdinalIgnoreCase))
                    ?.Value.Trim();

            // Skip forecast periods without valid minimum and maximum temperatures.
            if (!TryReadNumber(
                    ReadElement("air_temperature_minimum"),
                    out var minTemp)
                ||
                !TryReadNumber(
                    ReadElement("air_temperature_maximum"),
                    out var maxTemp))
            {
                continue;
            }

            var rainfall = ParseRainfall(
                ReadElement("precipitation_range"));

            var rainProbability = ParsePercentage(
                ReadText("probability_of_precipitation"));

            // Humidity is not supplied in the daily forecast periods
            // currently being parsed. Do not invent a value.
            var humidity = double.NaN;

            var day = new ForecastDay
            {
                ForecastDate = forecastDate,
                MinTemp = minTemp,
                MaxTemp = maxTemp,
                Humidity = humidity,
                RainfallMm = rainfall,
                RainProbability = rainProbability
            };

            var dateKey = DateOnly.FromDateTime(forecastDate);

            if (!dailyData.TryGetValue(dateKey, out var entries))
            {
                entries = new List<ForecastDay>();
                dailyData[dateKey] = entries;
            }

            entries.Add(day);
        }

        // Combine multiple forecast periods belonging to the same date.
        foreach (var entry in dailyData.OrderBy(x => x.Key))
        {
            var records = entry.Value;

            result.Add(new ForecastDay
            {
                ForecastDate = entry.Key.ToDateTime(TimeOnly.MinValue),

                MinTemp = records.Min(x => x.MinTemp),

                MaxTemp = records.Max(x => x.MaxTemp),

                Humidity = records
                    .Select(x => x.Humidity)
                    .Where(x => !double.IsNaN(x) && !double.IsInfinity(x))
                    .DefaultIfEmpty(double.NaN)
                    .Average(),

                RainfallMm = records
                    .Select(x => x.RainfallMm)
                    .Where(x => !double.IsNaN(x) && !double.IsInfinity(x))
                    .DefaultIfEmpty(double.NaN)
                    .Max(),

                RainProbability = records
                    .Select(x => x.RainProbability)
                    .Where(x => !double.IsNaN(x) && !double.IsInfinity(x))
                    .DefaultIfEmpty(double.NaN)
                    .Max()
            });
        }

        return result;
    }

    private static bool TryReadNumber(
        string? value,
        out double number)
    {
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number);
    }

    /// <summary>
    /// Parses the first number in a precipitation range such as "0 to 3 mm".
    /// This returns the lower bound, not the maximum expected rainfall.
    /// </summary>
    private static double ParseRainfall(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return double.NaN;
        }

        var firstNumber = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return TryReadNumber(firstNumber, out var rainfall)
            ? rainfall
            : double.NaN;
    }

    private static double ParsePercentage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return double.NaN;
        }

        value = value.Replace("%", "").Trim();

        return TryReadNumber(value, out var percentage)
            ? percentage
            : double.NaN;
    }

    private static string FormatOptionalValue(
        double value,
        string suffix)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? "N/A"
            : $"{value:0.#}{suffix}";
    }

    /// <summary>
    /// Generates sample records for offline demonstrations.
    /// These values are illustrative and are not actual BOM forecasts.
    /// </summary>
    private static List<ForecastDay> GenerateSampleForecast()
    {
        var random = new Random();
        var start = DateTime.Today;

        return Enumerable.Range(0, 5)
            .Select(i => new ForecastDay
            {
                ForecastDate = start.AddDays(i),
                MinTemp = 12 + random.Next(0, 6),
                MaxTemp = 20 + random.Next(0, 8),
                Humidity = 40 + random.Next(0, 30),
                RainfallMm = random.Next(0, 15),
                RainProbability = random.Next(5, 70)
            })
            .ToList();
    }
}