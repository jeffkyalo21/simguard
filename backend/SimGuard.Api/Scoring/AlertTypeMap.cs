using System.Text.Json;
using SimGuard.Api.Common;

namespace SimGuard.Api.Scoring;

// Alert type from the top reasons of the scored day that raised or last
// raised the alert (backend/docs/scoring-pipeline.md, "Alert type").
//
// Only reasons that increase risk (direction "up") count. Each mapped
// feature names a type, and the type with the highest precedence among all
// such reasons wins, whatever their order:
//
//   1. inactiveBilling  a billing feature above normal (value > 0)
//   2. sharing          hotspot features
//   3. reselling        offpeak_ratio, data_session_count, or a volume
//                       feature clearly above normal (value > 0.5)
//   4. ghostUsage       a volume feature at or below normal (value <= 0)
//   5. other            nothing mapped
//
// Values are the scaled values the model saw (0 is the SIM's normal). A
// volume value between 0 and 0.5 and a billing value at or below 0 decide
// nothing.
//
// Precedence, not contribution order, decides because the low-volume
// features (peak_mb, bytes_per_session) rank first for reselling, ghost
// usage and inactive billing alike; the specific features after them tell
// the patterns apart. Checked against the generator labels on the demo data
// (scoring-pipeline.md). Features not in the table (geo, roaming, tier and
// cohort_z_iso_anomaly_score) do not decide.
public static class AlertTypeMap
{
    private const string VolumeBySign = "volume";
    private const string BillingAbove = "billing";

    // Scaled value above which a volume feature means reselling.
    public const double ResellingVolumeMin = 0.5;

    public static readonly IReadOnlyDictionary<string, string> Features = new Dictionary<string, string>
    {
        // Billed without matching usage.
        ["billed_mb"] = BillingAbove,
        ["billing_gap_mb"] = BillingAbove,
        ["billing_ratio"] = BillingAbove,

        // Tethering to other devices.
        ["hotspot_sessions"] = AlertTypes.Sharing,
        ["hotspot_session_ratio"] = AlertTypes.Sharing,
        ["hotspot_sum_7d"] = AlertTypes.Sharing,
        ["hotspot_sum_14d"] = AlertTypes.Sharing,

        // Night-time resale traffic in many sessions (evaluation report:
        // offpeak_ratio separates data_reselling).
        ["offpeak_ratio"] = AlertTypes.Reselling,
        ["data_session_count"] = AlertTypes.Reselling,

        // Volume: reselling when value > 0.5, ghostUsage when value <= 0.
        ["total_mb"] = VolumeBySign,
        ["total_download_mb"] = VolumeBySign,
        ["total_upload_mb"] = VolumeBySign,
        ["peak_mb"] = VolumeBySign,
        ["offpeak_mb"] = VolumeBySign,
        ["bytes_per_session"] = VolumeBySign,
        ["vol_rolling_7d_mean"] = VolumeBySign,
        ["vol_rolling_7d_std"] = VolumeBySign,
        ["vol_rolling_14d_mean"] = VolumeBySign,
        ["vol_rolling_14d_std"] = VolumeBySign,
        ["vol_zscore_7d"] = VolumeBySign,
        ["vol_zscore_14d"] = VolumeBySign,
        ["trend_slope_14d"] = VolumeBySign,
        ["cohort_z_total_mb"] = VolumeBySign,
        ["cohort_z_vol_zscore_7d"] = VolumeBySign,
        ["cohort_z_vol_zscore_14d"] = VolumeBySign
    };

    // Highest precedence first.
    public static readonly IReadOnlyList<string> Precedence =
    [
        AlertTypes.InactiveBilling,
        AlertTypes.Sharing,
        AlertTypes.Reselling,
        AlertTypes.GhostUsage
    ];

    // Reasons in the stored format: [{feature, value, contribution, direction}].
    public static string For(JsonElement reasons)
    {
        if (reasons.ValueKind != JsonValueKind.Array)
        {
            return AlertTypes.Other;
        }

        var found = new HashSet<string>();

        foreach (var reason in reasons.EnumerateArray())
        {
            if (!reason.TryGetProperty("direction", out var d) || d.GetString() != "up"
                || !reason.TryGetProperty("feature", out var f)
                || f.GetString() is not { } feature
                || !Features.TryGetValue(feature, out var type))
            {
                continue;
            }

            var value = Number(reason, "value");
            var decided = type switch
            {
                BillingAbove => value > 0 ? AlertTypes.InactiveBilling : null,
                VolumeBySign => value > ResellingVolumeMin ? AlertTypes.Reselling
                    : value <= 0 ? AlertTypes.GhostUsage
                    : null,
                _ => type
            };

            if (decided is not null)
            {
                found.Add(decided);
            }
        }

        return Precedence.FirstOrDefault(found.Contains) ?? AlertTypes.Other;
    }

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
