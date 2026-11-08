namespace SimGuard.Api.Scoring;

public static class ScoringTriggers
{
    public const string Schedule = "schedule";
    public const string Manual = "manual";
    public const string Import = "import";

    public static readonly IReadOnlyList<string> All = [Schedule, Manual, Import];
}

public static class ScoringRunStatuses
{
    // Waiting for the running run to finish. Triggers that arrive meanwhile
    // are merged into the queued run.
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";

    // Stopped after a failure with some SIM-days scored.
    public const string Partial = "partial";

    // Stopped before any SIM-day was scored, or the ML service was down.
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All = [Queued, Running, Succeeded, Partial, Failed];

    public static bool IsFinished(string status) => status is Succeeded or Partial or Failed;
}

// Configuration section "Scoring".
public sealed class ScoringOptions
{
    public const string Section = "Scoring";

    // Scheduled daily run, Africa/Nairobi local time (HH:mm).
    public string DailyRunTimeNairobi { get; set; } = "02:00";

    // Off: no scheduled run and no run after a usage import. Run now
    // still works.
    public bool Enabled { get; set; } = true;

    // SIMs per /v1/score request; capped at the contract maximum (5000).
    public int BatchSize { get; set; } = 1000;

    // Alert rules (Scoring/AlertRules.cs). Not defined in the ML docs; these
    // are the issue #14 defaults.
    public int SustainedWindowDays { get; set; } = 5;
    public int SustainedMinDays { get; set; } = 3;
    public int FalsePositiveCooldownDays { get; set; } = 7;
}

// Configuration section "Alerts": conditions on top of the sustained rule.
public sealed class AlertOptions
{
    public const string Section = "Alerts";

    // A sustained window raises or updates an alert only when at least this
    // many of its days are flag (score above the flag threshold). With 1, a
    // SIM that is only ever escalated keeps its scores but gets no alert.
    public int MinFlaggedDaysInWindow { get; set; } = 1;

    // Inactive billing check on the stored daily usage of the window, ahead
    // of the reasons mapping. Chosen from the demo data: billed days with
    // usage at most 1 MB occur only on SIMs labelled inactive_billing (median
    // usage 0 MB, billed about 2000 MB). 0 days turns the check off.
    public double InactiveBillingMaxUsageMb { get; set; } = 1;
    public double InactiveBillingMinBilledMb { get; set; } = 100;
    public int InactiveBillingMinDays { get; set; } = 2;
}
