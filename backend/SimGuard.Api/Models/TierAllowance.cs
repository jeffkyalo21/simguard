namespace SimGuard.Api.Models;

// What one plan tier includes per 30-day billing cycle and what it costs.
// One row per tier (field, manager, professional, support).
public class TierAllowance
{
    public string Tier { get; set; } = string.Empty;

    // Data bundle in MB (1 GB = 1024 MB, as in ml-service TIER_BUNDLE_MB).
    public int DataMb { get; set; }

    public int VoiceMinutes { get; set; }
    public int Sms { get; set; }

    // KES per billing cycle.
    public decimal MonthlyCost { get; set; }
}
