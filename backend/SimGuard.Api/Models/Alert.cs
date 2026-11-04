using System.Text.Json;

namespace SimGuard.Api.Models;

// One alert raised for a SIM. Values for Type, Status, RiskLevel and
// Resolution are listed in Common/AlertVocabulary.cs and enforced by check
// constraints.
public class Alert
{
    public int Id { get; set; }
    public int SimId { get; set; }
    public string Type { get; set; } = string.Empty;

    // 0 to 1, two decimals.
    public decimal RiskScore { get; set; }

    // Taken from the bucket that raised the alert; not changed by reviews.
    public string RiskLevel { get; set; } = string.Empty;

    public string Status { get; set; } = "flagged";
    public DateTime CreatedAt { get; set; }

    // JSON array of {feature, value, contribution, direction}.
    public JsonDocument Reasons { get; set; } = JsonDocument.Parse("[]");

    public string? ModelVersion { get; set; }

    // As-of dates of the scored days (issue #14): the first sustained day
    // that raised the alert and the latest one that updated it. CreatedAt
    // stays the real time the alert was stored, which differs during a
    // backfill. Null for alerts created before scoring existed.
    public DateOnly? DetectedOn { get; set; }
    public DateOnly? LastDetectedOn { get; set; }

    // Set when the alert is cleared, reset when it is reopened.
    public DateTime? ResolvedAt { get; set; }
    public string? Resolution { get; set; }
    public string? ResolutionNote { get; set; }
    public int? ResolvedByUserId { get; set; }

    public Sim Sim { get; set; } = null!;
    public AppUser? ResolvedByUser { get; set; }
}
