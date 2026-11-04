namespace SimGuard.Api.Models;

public class Sim
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string Iccid { get; set; } = string.Empty;
    public int CarrierId { get; set; }
    public string PlanType { get; set; } = string.Empty;
    public decimal MonthlyCost { get; set; }
    public string Status { get; set; } = "available";

    // ML plan tier: field, manager, professional or support. It picks the
    // cohort the ML service compares the SIM with. Null until set.
    public string? Tier { get; set; }

    public DateTime CreatedAt { get; set; }

    public Carrier Carrier { get; set; } = null!;

    public ICollection<Allocation> Allocations { get; set; }
        = new List<Allocation>();

    public ICollection<Alert> Alerts { get; set; }
        = new List<Alert>();

    public ICollection<AuditLog> AuditLogs { get; set; }
        = new List<AuditLog>();

    public ICollection<RiskScore> RiskScores { get; set; }
        = new List<RiskScore>();

    public ICollection<DailyUsage> DailyUsage { get; set; }
        = new List<DailyUsage>();
}