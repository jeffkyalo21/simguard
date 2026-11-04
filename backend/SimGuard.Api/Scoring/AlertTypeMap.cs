using System.Text.Json;
using SimGuard.Api.Common;

namespace SimGuard.Api.Scoring;

// Alert type from the top reasons of the scored day that raised or last
// raised the alert (backend/docs/scoring-pipeline.md, "Alert type").
//
// The reasons are tried in order of contribution, largest first, and only
// those that increase risk (direction "up") count. The first feature found
// in the table below decides the type. Features not in the table (geo,
// roaming, session shape, tier and cohort_z_iso_anomaly_score) do not
// decide; when no reason decides, the type is "other".
//
// Volume features depend on the sign of the scaled value the model saw:
// above the SIM's normal (value > 0) points to reselling bursts, at or
// below it (value <= 0) to ghost usage (a SIM kept alive with almost no
// traffic). The patterns follow the anomaly types in
// ml-service/docs/evaluation_report.md.
public static class AlertTypeMap
{
    private const string VolumeBySign = "volume";

    public static readonly IReadOnlyDictionary<string, string> Features = new Dictionary<string, string>
    {
        // Billed without matching usage.
        ["billed_mb"] = AlertTypes.InactiveBilling,
        ["billing_gap_mb"] = AlertTypes.InactiveBilling,
        ["billing_ratio"] = AlertTypes.InactiveBilling,

        // Tethering to other devices.
        ["hotspot_sessions"] = AlertTypes.Sharing,
        ["hotspot_session_ratio"] = AlertTypes.Sharing,
        ["hotspot_sum_7d"] = AlertTypes.Sharing,
        ["hotspot_sum_14d"] = AlertTypes.Sharing,

        // Night-time resale traffic (evaluation report: offpeak_ratio
        // separates data_reselling, AUC 0.94).
        ["offpeak_ratio"] = AlertTypes.Reselling,

        // Volume: reselling when value > 0, ghostUsage when value <= 0.
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

    // Reasons in the stored format: [{feature, value, contribution, direction}].
    public static string For(JsonElement reasons)
    {
        if (reasons.ValueKind != JsonValueKind.Array)
        {
            return AlertTypes.Other;
        }

        var ordered = reasons.EnumerateArray()
            .Where(r => r.TryGetProperty("direction", out var d) && d.GetString() == "up")
            .OrderByDescending(r => Number(r, "contribution"));

        foreach (var reason in ordered)
        {
            if (!reason.TryGetProperty("feature", out var f)
                || f.GetString() is not { } feature
                || !Features.TryGetValue(feature, out var type))
            {
                continue;
            }

            if (type != VolumeBySign)
            {
                return type;
            }

            return Number(reason, "value") > 0 ? AlertTypes.Reselling : AlertTypes.GhostUsage;
        }

        return AlertTypes.Other;
    }

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
