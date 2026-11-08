namespace SimGuard.Api.Models;

// Tier recommendations (issue #14). Values are in
// TierRecommendations/TierRecommendationVocabulary.cs and enforced by check
// constraints. Nothing is applied automatically: a tier changes only through
// PATCH /api/sims/{id}/tier.

// Single row (Id 1): how often a recommendation run may start.
public class TierRecommendationSettings
{
    public int Id { get; set; } = 1;

    // 90 to 365.
    public int IntervalDays { get; set; } = 90;

    public int? UpdatedByUserId { get; set; }

    // Email of the user who last changed it, null while it is the default.
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public AppUser? UpdatedByUser { get; set; }
}

public class TierRecommendationRun
{
    public int Id { get; set; }

    // manual or automatic.
    public string Trigger { get; set; } = string.Empty;

    // running, succeeded, partial or failed.
    public string Status { get; set; } = "running";

    public int? RequestedByUserId { get; set; }

    // The user's email for a manual run, "system" for an automatic one.
    public string RequestedBy { get; set; } = "system";

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    // 30-day cycle (from 2025-01-01) holding the latest usage date.
    public int? BillingCycle { get; set; }

    public int SimsEvaluated { get; set; }
    public int RecommendationsCreated { get; set; }
    public string? ErrorSummary { get; set; }

    public AppUser? RequestedByUser { get; set; }
}

public class TierRecommendation
{
    public int Id { get; set; }
    public int SimId { get; set; }
    public int RunId { get; set; }

    public string CurrentTier { get; set; } = string.Empty;
    public string RecommendedTier { get; set; } = string.Empty;

    // upgrade or downgrade.
    public string Recommendation { get; set; } = string.Empty;

    // Plain sentence built from the ML forecast.
    public string Reason { get; set; } = string.Empty;

    public int BillingCycle { get; set; }
    public double? ProjectedOverageMb { get; set; }
    public int? DaysToDepletion { get; set; }

    // open, dismissed or applied.
    public string Status { get; set; } = "open";

    public DateTime CreatedAt { get; set; }
    public DateTime? DismissedAt { get; set; }
    public int? DismissedByUserId { get; set; }
    public DateTime? AppliedAt { get; set; }
    public int? AppliedByUserId { get; set; }

    public Sim Sim { get; set; } = null!;
    public TierRecommendationRun Run { get; set; } = null!;
    public AppUser? DismissedByUser { get; set; }
    public AppUser? AppliedByUser { get; set; }
}
