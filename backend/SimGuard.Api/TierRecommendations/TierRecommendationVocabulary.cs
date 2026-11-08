namespace SimGuard.Api.TierRecommendations;

public static class TierRunTriggers
{
    public const string Manual = "manual";
    public const string Automatic = "automatic";
}

public static class TierRunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";

    // Stopped after a failed batch with some SIMs evaluated.
    public const string Partial = "partial";
    public const string Failed = "failed";
}

public static class TierRecommendationStatuses
{
    public const string Open = "open";
    public const string Dismissed = "dismissed";

    // The SecurityOfficer set the SIM to the recommended tier (issue #14).
    public const string Applied = "applied";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Open, Dismissed, Applied };
}

public static class TierRecommendationKinds
{
    public const string Upgrade = "upgrade";
    public const string Downgrade = "downgrade";
}

// Configuration section "TierRecommendations".
public sealed class TierRecommendationOptions
{
    public const string Section = "TierRecommendations";

    public const int MinIntervalDays = 90;
    public const int MaxIntervalDays = 365;
    public const int DefaultIntervalDays = 90;

    // A SIM whose recommendation was dismissed is not given the same
    // recommended tier again for this many days.
    public const int DismissedCooldownDays = 90;

    // An automatic run starts when the last run started more than
    // IntervalDays plus this many days ago.
    public int AutoRunGraceDays { get; set; } = 14;

    // How often the background service checks whether an automatic run is due.
    public double CheckIntervalHours { get; set; } = 24;

    // SIMs per /v1/recommend-tier request (contract maximum 200).
    public int BatchSize { get; set; } = 200;
}

// Fixed 30-day billing cycles counted from 2025-01-01, as in
// ml-service/app/tiering.py (SIM_START, CYCLE_LEN).
public static class BillingCycles
{
    public static readonly DateOnly SimStart = new(2025, 1, 1);
    public const int Length = 30;

    public static int For(DateOnly date) => Math.Max(0, (date.DayNumber - SimStart.DayNumber) / Length);

    public static DateOnly Start(int cycle) => SimStart.AddDays(cycle * Length);
}
