namespace WeatherDashboard.Models;

/// <summary>
/// A trackable Australian location. BomProductId is the BOM forecast product
/// code (e.g. "IDN10064" style codes are per-state; a real implementation
/// narrows this to a specific town/AAC code within that product).
/// </summary>
public class Location
{
    public string LocationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string BomProductId { get; set; } = string.Empty;

    public override string ToString() => $"{Name}, {State}";
}
