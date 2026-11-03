namespace SimGuard.Api.Common;

// Values stored in alerts and risk_scores. They mirror the check constraints
// in SimGuardDbContext and backend/docs/api-alerts-risk-audit.md.
public static class AlertTypes
{
    public const string InactiveBilling = "inactiveBilling";
    public const string Sharing = "sharing";
    public const string Reselling = "reselling";
    public const string GhostUsage = "ghostUsage";

    // The model flags SIM-days without a category; issue #14 assigns one
    // from the top reasons and falls back to this.
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
        [InactiveBilling, Sharing, Reselling, GhostUsage, Other];
}

public static class AlertStatuses
{
    public const string Flagged = "flagged";
    public const string Escalated = "escalated";
    public const string Cleared = "cleared";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Flagged, Escalated, Cleared };

    // Allowed review transitions. Anything else is a 409.
    public static bool CanMove(string from, string to) => (from, to) switch
    {
        (Flagged, Cleared) => true,
        (Escalated, Cleared) => true,
        (Cleared, Escalated) => true,
        (Escalated, Flagged) => true,
        _ => false
    };
}

public static class AlertResolutions
{
    public const string ConfirmedMisuse = "confirmedMisuse";
    public const string FalsePositive = "falsePositive";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { ConfirmedMisuse, FalsePositive };

    public const int MaxNoteLength = 500;
}

public static class RiskBuckets
{
    public const string Clear = "clear";
    public const string Escalated = "escalated";
    public const string Flag = "flag";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Clear, Escalated, Flag };
}

// The backend never compares probabilities with thresholds. The level
// follows the bucket the model assigned.
public static class RiskLevels
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    public static string FromBucket(string bucket) => bucket switch
    {
        RiskBuckets.Flag => High,
        RiskBuckets.Escalated => Medium,
        _ => Low
    };

    // An alert opened as flagged came from the flag bucket, one opened as
    // escalated from the escalated bucket.
    public static string ForNewAlert(string status) =>
        status == AlertStatuses.Escalated ? Medium : High;
}
