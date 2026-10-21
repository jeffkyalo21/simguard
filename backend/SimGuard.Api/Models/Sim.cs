namespace SimGuard.Api.Models;

public class Sim
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string Iccid { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public decimal MonthlyCost { get; set; }
    public string Status { get; set; } = "available";
    public DateTime CreatedAt { get; set; }

    public ICollection<Allocation> Allocations { get; set; }
        = new List<Allocation>();

    public ICollection<Alert> Alerts { get; set; }
        = new List<Alert>();

    public ICollection<AuditLog> AuditLogs { get; set; }
        = new List<AuditLog>();
}