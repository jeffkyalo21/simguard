using System.Text.Json;

namespace SimGuard.Api.Models;

public class AuditLog
{
    public int Id { get; set; }

    // Legacy link to an Employee. Rows written by the API leave it null and
    // record the signed-in account in ActorUserId and ActorEmail instead.
    public int? UserId { get; set; }

    public int? ActorUserId { get; set; }

    // Snapshot of the actor's email when the row was written.
    public string? ActorEmail { get; set; }

    public string Action { get; set; } = string.Empty;
    public int? AffectedSimId { get; set; }
    public JsonDocument? OldValues { get; set; }
    public JsonDocument? NewValues { get; set; }
    public DateTime Timestamp { get; set; }

    public Employee? User { get; set; }
    public AppUser? ActorUser { get; set; }
    public Sim? AffectedSim { get; set; }
}
