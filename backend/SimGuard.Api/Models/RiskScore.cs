using System.Text.Json;

namespace SimGuard.Api.Models;

// The model's daily score for one SIM. One row per SIM per date.
public class RiskScore
{
    public long Id { get; set; }
    public int SimId { get; set; }
    public DateOnly Date { get; set; }

    // 0 to 1.
    public decimal Probability { get; set; }

    // clear, escalated or flag (see Common/AlertVocabulary.cs).
    public string Bucket { get; set; } = string.Empty;

    // JSON array of {feature, value, contribution, direction}.
    public JsonDocument Reasons { get; set; } = JsonDocument.Parse("[]");

    public string? ModelVersion { get; set; }
    public DateTime CreatedAt { get; set; }

    public Sim Sim { get; set; } = null!;
}
