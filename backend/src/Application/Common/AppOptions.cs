namespace PassManager.Application.Common;

public class AppOptions
{
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";
    public int EmailConfirmationTokenExpiryHours { get; set; } = 24;
}
