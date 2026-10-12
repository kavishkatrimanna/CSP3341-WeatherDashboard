namespace WeatherDashboard.Models;

public class User
{
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    public bool IsInRole(UserRole role) => Role == role;
}
