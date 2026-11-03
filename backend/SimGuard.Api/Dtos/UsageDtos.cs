using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

// One day of usage. Fields are the ML daily usage contract in camelCase.
public record DailyUsageItem(
    DateOnly Date,
    double TotalDownloadMb,
    double TotalUploadMb,
    long DataSessionCount,
    long DataDurationS,
    long HotspotSessions,
    long PublicApnSessions,
    long UniqueTowers,
    long GeoFlagCount,
    double PeakMb,
    double OffpeakMb,
    double TotalMb,
    long RoamingSessions,
    double BilledMb,
    string Tier,
    double VoiceMinutesUsed,
    long SmsCount,
    double VoiceMinutesRemaining,
    long SmsRemaining)
{
    public static readonly Expression<Func<DailyUsage, DailyUsageItem>> Projection = u =>
        new DailyUsageItem(
            u.Date,
            u.TotalDownloadMb,
            u.TotalUploadMb,
            u.DataSessionCount,
            u.DataDurationS,
            u.HotspotSessions,
            u.PublicApnSessions,
            u.UniqueTowers,
            u.GeoFlagCount,
            u.PeakMb,
            u.OffpeakMb,
            u.TotalMb,
            u.RoamingSessions,
            u.BilledMb,
            u.Tier,
            u.VoiceMinutesUsed,
            u.SmsCount,
            u.VoiceMinutesRemaining,
            u.SmsRemaining);
}

// Sums over the returned days.
public record UsageTotals(
    int Days,
    double TotalMb,
    double BilledMb,
    double VoiceMinutesUsed,
    long SmsCount);

public record SimUsageResponse(
    int SimId,
    DateOnly From,
    DateOnly To,
    UsageTotals Totals,
    IReadOnlyList<DailyUsageItem> Days);
