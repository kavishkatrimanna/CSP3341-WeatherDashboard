using Microsoft.Data.Sqlite;
using WeatherDashboard.Models;

namespace WeatherDashboard.Repositories;

/// <summary>
/// Persists tracked locations and cached forecasts to a local SQLite file so the
/// dashboard can still show the last-known forecast if the BOM feed is down.
/// </summary>
public class WeatherRepository : IWeatherRepository
{
    private readonly string _connectionString;

    public WeatherRepository(string dbFileName)
    {
        _connectionString = $"Data Source={dbFileName}";
        EnsureSchema();
        SeedDefaultLocationsIfEmpty();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Locations (
                LocationId   TEXT PRIMARY KEY,
                Name         TEXT NOT NULL,
                State        TEXT NOT NULL,
                Latitude     REAL NOT NULL,
                Longitude    REAL NOT NULL,
                BomProductId TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ForecastCache (
                LocationId       TEXT NOT NULL,
                ForecastDate     TEXT NOT NULL,
                MinTemp          REAL,
                MaxTemp          REAL,
                Humidity         REAL,
                RainfallMm       REAL,
                RainProbability  REAL,
                PRIMARY KEY (LocationId, ForecastDate)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void SeedDefaultLocationsIfEmpty()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var count = (long)new SqliteCommand("SELECT COUNT(*) FROM Locations", conn).ExecuteScalar()!;
        if (count > 0) return;

        var defaults = new[]
        {
            new Location { LocationId = "perth",  Name = "Perth",  State = "WA", Latitude = -31.95, Longitude = 115.86, BomProductId = "IDW14199" },
            new Location { LocationId = "sydney",  Name = "Sydney", State = "NSW", Latitude = -33.87, Longitude = 151.21, BomProductId = "IDN11060" },
        };
        foreach (var loc in defaults)
            AddTrackedLocationAsync(loc).GetAwaiter().GetResult();
    }

    public Task<List<Location>> GetTrackedLocationsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT LocationId, Name, State, Latitude, Longitude, BomProductId FROM Locations";

        var result = new List<Location>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Location
            {
                LocationId = reader.GetString(0),
                Name = reader.GetString(1),
                State = reader.GetString(2),
                Latitude = reader.GetDouble(3),
                Longitude = reader.GetDouble(4),
                BomProductId = reader.GetString(5)
            });
        }
        return Task.FromResult(result);
    }

    public Task AddTrackedLocationAsync(Location location)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO Locations (LocationId, Name, State, Latitude, Longitude, BomProductId)
            VALUES ($id, $name, $state, $lat, $lon, $bom)
            """;
        cmd.Parameters.AddWithValue("$id", location.LocationId);
        cmd.Parameters.AddWithValue("$name", location.Name);
        cmd.Parameters.AddWithValue("$state", location.State);
        cmd.Parameters.AddWithValue("$lat", location.Latitude);
        cmd.Parameters.AddWithValue("$lon", location.Longitude);
        cmd.Parameters.AddWithValue("$bom", location.BomProductId);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task RemoveTrackedLocationAsync(string locationId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Locations WHERE LocationId = $id";
        cmd.Parameters.AddWithValue("$id", locationId);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task SaveForecastAsync(Location location, List<ForecastDay> forecast)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        foreach (var day in forecast)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO ForecastCache
                    (LocationId, ForecastDate, MinTemp, MaxTemp, Humidity, RainfallMm, RainProbability)
                VALUES ($loc, $date, $min, $max, $hum, $rain, $prob)
                """;
            cmd.Parameters.AddWithValue("$loc", location.LocationId);
            cmd.Parameters.AddWithValue(
                "$date",
                day.ForecastDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$min", ToSqliteValue(day.MinTemp));
            cmd.Parameters.AddWithValue("$max", ToSqliteValue(day.MaxTemp));
            cmd.Parameters.AddWithValue("$hum", ToSqliteValue(day.Humidity));
            cmd.Parameters.AddWithValue("$rain", ToSqliteValue(day.RainfallMm));
            cmd.Parameters.AddWithValue("$prob", ToSqliteValue(day.RainProbability));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return Task.CompletedTask;
    }

    public Task<List<ForecastDay>> GetCachedForecastAsync(string locationId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ForecastDate, MinTemp, MaxTemp, Humidity, RainfallMm, RainProbability
            FROM ForecastCache WHERE LocationId = $loc ORDER BY ForecastDate
            """;
        cmd.Parameters.AddWithValue("$loc", locationId);

        var result = new List<ForecastDay>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ForecastDay
            {
                ForecastDate = DateTime.Parse(reader.GetString(0)),
                MinTemp = reader.GetDouble(1),
                MaxTemp = reader.GetDouble(2),
                Humidity = ReadNullableDouble(reader, 3),
                RainfallMm = ReadNullableDouble(reader, 4),
                RainProbability = ReadNullableDouble(reader, 5)
            });
        }
        return Task.FromResult(result);
    }


    // SQLite does not support IEEE NaN or infinity as REAL values.
    // Store missing/non-finite measurements as SQL NULL instead.
    private static object ToSqliteValue(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? DBNull.Value
            : value;
    }

    // Convert SQL NULL back to the model's sentinel for missing measurements.
    private static double ReadNullableDouble(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? double.NaN : reader.GetDouble(ordinal);
    }

    public Task ClearCacheAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        new SqliteCommand("DELETE FROM ForecastCache", conn).ExecuteNonQuery();
        return Task.CompletedTask;
    }
}
