namespace SimGuard.Api.Models;

public class Alert
{
    public int Id { get; set; }
    public int SimId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public decimal RiskScore { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public string Status { get; set; } = "open";

    public Sim Sim { get; set; } = null!;
}