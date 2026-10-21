using System.Text.Json;

namespace SimGuard.Api.Models;

public class AuditLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int? AffectedSimId { get; set; }
    public JsonDocument? OldValues { get; set; }
    public JsonDocument? NewValues { get; set; }
    public DateTime Timestamp { get; set; }

    public Employee User { get; set; } = null!;
    public Sim? AffectedSim { get; set; }
}