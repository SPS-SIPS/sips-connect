namespace SIPS.Connect.Models;

public class HealthCheckResponse
{
    public string Status { get; set; } = "ok";
    public List<ComponentHealth> Components { get; set; } = new();
}

public class ComponentHealth
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "ok";
    public string EndpointStatus { get; set; } = "ok";
    public string HttpResult { get; set; } = string.Empty;
    public DateTime LastChecked { get; set; }
    public string? ErrorMessage { get; set; }
}
