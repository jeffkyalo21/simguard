namespace SimGuard.Api.Models;

// One SIM's usage for one calendar day. The columns are the ML daily usage
// contract (ml-service/docs/daily_usage_schema.md), same names in snake_case.
// Units: data in MB, durations in seconds, voice in minutes.
public class DailyUsage
{
    public long Id { get; set; }
    public int SimId { get; set; }
    public DateOnly Date { get; set; }

    public double TotalDownloadMb { get; set; }
    public double TotalUploadMb { get; set; }
    public long DataSessionCount { get; set; }
    public long DataDurationS { get; set; }
    public long HotspotSessions { get; set; }
    public long PublicApnSessions { get; set; }
    public long UniqueTowers { get; set; }
    public long GeoFlagCount { get; set; }
    public double PeakMb { get; set; }
    public double OffpeakMb { get; set; }
    public double TotalMb { get; set; }
    public long RoamingSessions { get; set; }
    public double BilledMb { get; set; }

    // field, manager, professional or support.
    public string Tier { get; set; } = string.Empty;

    public double VoiceMinutesUsed { get; set; }
    public long SmsCount { get; set; }

    // Can be negative in the source data (bundle overrun).
    public double VoiceMinutesRemaining { get; set; }
    public long SmsRemaining { get; set; }

    public Sim Sim { get; set; } = null!;
}
