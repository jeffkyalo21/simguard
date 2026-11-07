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
    IReadOnlyList<DailyUsageItem> Days,
    DateOnly? AsOf);

// GET /sims/{id}/usage/cycle: one day of the billing cycle with the running
// totals since the cycle started.
public record CycleUsageDay(
    DateOnly Date,
    double DataMb,
    double CumulativeDataMb,
    double VoiceMinutes,
    long Sms);

public record CycleUsageUsed(
    double DataMb,
    double VoiceMinutes,
    long Sms);

// AsOf is the latest daily usage date of the whole fleet. Cycle, CycleStart
// and CycleEnd are the 30-day billing cycle (counted from 2025-01-01) that
// holds AsOf; all of them are null while there is no usage at all.
// DaysElapsed counts the cycle days up to AsOf. Tier is the SIM's tier, or
// the tier on its latest usage row in the cycle when the SIM has none.
public record SimCycleUsageResponse(
    int SimId,
    DateOnly? AsOf,
    int? Cycle,
    DateOnly? CycleStart,
    DateOnly? CycleEnd,
    int CycleLength,
    int DaysElapsed,
    string? Tier,
    TierAllowanceResponse? Allowance,
    CycleUsageUsed Used,
    IReadOnlyList<CycleUsageDay> Days);

// GET /usage/analytics. Paid data is each SIM's tier bundle prorated over
// the period (bundle * days / 30); unused data is the paid data a SIM did
// not use (never negative for one SIM).
public record UsageAnalyticsTotals(
    int SimCount,
    double DataUsedMb,
    double DataPaidMb,
    double UnusedMb,
    int UtilisationPercent);

public record UsageDailyTotal(DateOnly Date, double DataMb);

// Name is the department or tier; null groups SIMs with no current
// allocation (department) or no tier.
public record UsageGroup(
    string? Name,
    int SimCount,
    double DataUsedMb,
    double DataPaidMb);

public record UsageSimRow(
    int SimId,
    string PhoneNumber,
    string? EmployeeName,
    string? DepartmentName,
    string? Tier,
    string Carrier,
    double DataUsedMb,
    double DataPaidMb,
    int UtilisationPercent);

public record UsageAnalyticsResponse(
    int Days,
    DateOnly? AsOf,
    DateOnly? From,
    DateOnly? To,
    UsageAnalyticsTotals Totals,
    IReadOnlyList<UsageDailyTotal> Daily,
    IReadOnlyList<UsageGroup> ByDepartment,
    IReadOnlyList<UsageGroup> ByTier,
    IReadOnlyList<UsageSimRow> TopSims,
    IReadOnlyList<UsageSimRow> LeastUsedSims);

// Dashboard dataWaste: paid against used data for the period.
public record DataWasteDepartment(string? Name, double UnusedMb);

public record DataWasteResponse(
    double PaidMb,
    double UsedMb,
    double UnusedMb,
    int UnusedPercent,
    IReadOnlyList<DataWasteDepartment> Departments);

// Dashboard tierChanges: open tier recommendations by kind.
public record TierChangeCount(string Recommendation, int Count);
