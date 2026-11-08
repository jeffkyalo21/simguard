namespace SimGuard.Api.Models;

// One scoring run (issue #14). Values of Trigger and Status are in
// Scoring/ScoringVocabulary.cs and enforced by check constraints.
public class ScoringRun
{
    public int Id { get; set; }

    // schedule, manual or import.
    public string Trigger { get; set; } = string.Empty;

    // queued, running, succeeded, partial or failed.
    public string Status { get; set; } = "queued";

    // Rescore SIM-days that already have a score.
    public bool Force { get; set; }

    // Null: every SIM. Otherwise only these SIMs (an import run).
    public int[]? ScopeSimIds { get; set; }

    // Null: no limit on that side.
    public DateOnly? ScopeFrom { get; set; }
    public DateOnly? ScopeTo { get; set; }

    // Other triggers merged into this run while it was queued.
    public int MergedRequests { get; set; }

    public int? RequestedByUserId { get; set; }

    // The user's email for a manual run, "system" otherwise.
    public string RequestedBy { get; set; } = "system";

    public DateTime RequestedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    // First and last as-of date scored in this run.
    public DateOnly? FirstDate { get; set; }
    public DateOnly? LastDate { get; set; }

    public int DatesScored { get; set; }
    public int SimDaysScored { get; set; }

    // Sent but returned in notFound by the ML service.
    public int SimDaysSkipped { get; set; }

    // Batches that failed (the run stops at the first one).
    public int Failures { get; set; }

    public int AlertsCreated { get; set; }
    public int AlertsUpdated { get; set; }

    public string? ErrorSummary { get; set; }
    public string? ModelVersion { get; set; }

    public AppUser? RequestedByUser { get; set; }
}
