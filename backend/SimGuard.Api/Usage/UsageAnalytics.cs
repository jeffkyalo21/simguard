using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.TierRecommendations;

namespace SimGuard.Api.Usage;

// Usage reads built on daily_usage and the tier allowance table (issue #14):
// the as-of date, one SIM's billing cycle, the fleet analytics of the Usage
// screen and the dashboard data waste.
//
// "Now" for usage is the as-of date, the latest daily_usage date of the
// fleet, never the clock: the demo data ends on 2025-03-31.
public static class UsageAnalytics
{
    public static readonly int[] AllowedDays = [7, 30, 90];
    private const int SimRowCount = 10;
    private const int WasteDepartmentCount = 4;

    public static Task<DateOnly?> AsOfAsync(SimGuardDbContext db, CancellationToken cancellationToken) =>
        db.DailyUsage.MaxAsync(u => (DateOnly?)u.Date, cancellationToken);

    public static async Task<SimCycleUsageResponse> CycleAsync(
        SimGuardDbContext db,
        int simId,
        string? simTier,
        CancellationToken cancellationToken)
    {
        var asOf = await AsOfAsync(db, cancellationToken);
        var allowances = await LoadAllowancesAsync(db, cancellationToken);

        if (asOf is null)
        {
            var noUsageTier = simTier;
            return new SimCycleUsageResponse(
                simId, null, null, null, null, BillingCycles.Length, 0, noUsageTier,
                noUsageTier is null ? null : allowances.GetValueOrDefault(noUsageTier),
                new CycleUsageUsed(0, 0, 0), []);
        }

        var cycle = BillingCycles.For(asOf.Value);
        var start = BillingCycles.Start(cycle);
        var end = start.AddDays(BillingCycles.Length - 1);

        var rows = await db.DailyUsage
            .AsNoTracking()
            .Where(u => u.SimId == simId && u.Date >= start && u.Date <= asOf.Value)
            .OrderBy(u => u.Date)
            .Select(u => new { u.Date, u.TotalMb, u.VoiceMinutesUsed, u.SmsCount, u.Tier })
            .ToListAsync(cancellationToken);

        var tier = simTier ?? rows.LastOrDefault()?.Tier;
        var cumulative = 0d;
        var days = new List<CycleUsageDay>(rows.Count);

        foreach (var row in rows)
        {
            cumulative += row.TotalMb;
            days.Add(new CycleUsageDay(row.Date, Round(row.TotalMb), Round(cumulative), Round(row.VoiceMinutesUsed), row.SmsCount));
        }

        var used = new CycleUsageUsed(
            Round(cumulative),
            Round(rows.Sum(r => r.VoiceMinutesUsed)),
            rows.Sum(r => r.SmsCount));

        return new SimCycleUsageResponse(
            simId,
            asOf,
            cycle,
            start,
            end,
            BillingCycles.Length,
            asOf.Value.DayNumber - start.DayNumber + 1,
            tier,
            tier is null ? null : allowances.GetValueOrDefault(tier),
            used,
            days);
    }

    public static async Task<UsageAnalyticsResponse> AnalyticsAsync(
        SimGuardDbContext db,
        int days,
        int? departmentId,
        string? tier,
        CancellationToken cancellationToken)
    {
        var period = await LoadPeriodAsync(db, days, departmentId, tier, cancellationToken);

        if (period is null)
        {
            return new UsageAnalyticsResponse(
                days, null, null, null, new UsageAnalyticsTotals(0, 0, 0, 0, 0), [], [], [], [], []);
        }

        var rows = period.Sims;
        var used = rows.Sum(r => r.DataUsedMb);
        var paid = rows.Sum(r => r.DataPaidMb);
        var unused = rows.Sum(Unused);

        return new UsageAnalyticsResponse(
            days,
            period.AsOf,
            period.From,
            period.AsOf,
            new UsageAnalyticsTotals(rows.Count, Round(used), Round(paid), Round(unused), Percent(used, paid)),
            period.Daily,
            Group(rows, r => r.DepartmentName),
            Group(rows, r => r.Tier),
            rows.OrderByDescending(r => r.DataUsedMb).ThenBy(r => r.SimId).Take(SimRowCount).ToList(),
            rows.Where(r => r.DataPaidMb > 0)
                .OrderBy(r => r.DataUsedMb / r.DataPaidMb)
                .ThenBy(r => r.DataUsedMb)
                .ThenBy(r => r.SimId)
                .Take(SimRowCount)
                .ToList());
    }

    // Paid against used data for the dashboard period; null without usage.
    public static async Task<(DataWasteResponse? Waste, DateOnly? AsOf)> DataWasteAsync(
        SimGuardDbContext db,
        int days,
        CancellationToken cancellationToken)
    {
        var period = await LoadPeriodAsync(db, days, null, null, cancellationToken);

        if (period is null)
        {
            return (null, null);
        }

        var rows = period.Sims;
        var paid = rows.Sum(r => r.DataPaidMb);
        var unused = rows.Sum(Unused);
        var departments = rows
            .GroupBy(r => r.DepartmentName)
            .Select(g => new DataWasteDepartment(g.Key, Round(g.Sum(Unused))))
            .Where(d => d.UnusedMb > 0)
            .OrderByDescending(d => d.UnusedMb)
            .Take(WasteDepartmentCount)
            .ToList();

        var waste = new DataWasteResponse(
            Round(paid),
            Round(rows.Sum(r => r.DataUsedMb)),
            Round(unused),
            Percent(unused, paid),
            departments);

        return (waste, period.AsOf);
    }

    private sealed record Period(
        DateOnly AsOf,
        DateOnly From,
        List<UsageSimRow> Sims,
        List<UsageDailyTotal> Daily);

    // Every SIM with usage in the `days` days up to the as-of date, after the
    // filters. Null when there is no usage at all.
    private static async Task<Period?> LoadPeriodAsync(
        SimGuardDbContext db,
        int days,
        int? departmentId,
        string? tier,
        CancellationToken cancellationToken)
    {
        var asOf = await AsOfAsync(db, cancellationToken);

        if (asOf is null)
        {
            return null;
        }

        var from = asOf.Value.AddDays(1 - days);
        var usage = db.DailyUsage.AsNoTracking().Where(u => u.Date >= from && u.Date <= asOf.Value);

        if (tier is not null)
        {
            usage = usage.Where(u => u.Sim.Tier == tier);
        }

        if (departmentId is not null)
        {
            usage = usage.Where(u => u.Sim.Allocations.Any(
                a => a.ReturnDate == null && a.Employee.DepartmentId == departmentId));
        }

        var perSim = await usage
            .GroupBy(u => u.SimId)
            .Select(g => new { SimId = g.Key, UsedMb = g.Sum(u => u.TotalMb) })
            .ToListAsync(cancellationToken);

        var daily = await usage
            .GroupBy(u => u.Date)
            .Select(g => new { Date = g.Key, DataMb = g.Sum(u => u.TotalMb) })
            .OrderBy(d => d.Date)
            .ToListAsync(cancellationToken);

        var ids = perSim.Select(s => s.SimId).ToArray();
        var sims = await db.Sims
            .AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new
            {
                s.Id,
                s.PhoneNumber,
                s.Tier,
                Carrier = s.Carrier.Name,
                Allocation = s.Allocations
                    .Where(a => a.ReturnDate == null)
                    .OrderByDescending(a => a.AssignmentDate)
                    .Select(a => new { EmployeeName = a.Employee.Name, DepartmentName = a.Employee.Department.Name })
                    .FirstOrDefault(),
            })
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var allowances = await LoadAllowancesAsync(db, cancellationToken);

        var rows = perSim
            .Select(s =>
            {
                var sim = sims[s.SimId];
                var bundle = sim.Tier is not null && allowances.TryGetValue(sim.Tier, out var allowance) ? allowance.DataMb : 0;
                var paid = (double)bundle * days / BillingCycles.Length;
                return new UsageSimRow(
                    s.SimId,
                    sim.PhoneNumber,
                    sim.Allocation?.EmployeeName,
                    sim.Allocation?.DepartmentName,
                    sim.Tier,
                    sim.Carrier,
                    Round(s.UsedMb),
                    Round(paid),
                    Percent(s.UsedMb, paid));
            })
            .ToList();

        return new Period(
            asOf.Value,
            from,
            rows,
            daily.Select(d => new UsageDailyTotal(d.Date, Round(d.DataMb))).ToList());
    }

    private static async Task<Dictionary<string, TierAllowanceResponse>> LoadAllowancesAsync(
        SimGuardDbContext db,
        CancellationToken cancellationToken) =>
        await db.TierAllowances
            .AsNoTracking()
            .Select(TierAllowanceResponse.Projection)
            .ToDictionaryAsync(t => t.Tier, cancellationToken);

    private static List<UsageGroup> Group(List<UsageSimRow> rows, Func<UsageSimRow, string?> key) =>
        rows.GroupBy(key)
            .Select(g => new UsageGroup(g.Key, g.Count(), Round(g.Sum(r => r.DataUsedMb)), Round(g.Sum(r => r.DataPaidMb))))
            .OrderByDescending(g => g.DataUsedMb)
            .ToList();

    private static double Unused(UsageSimRow row) => Math.Max(row.DataPaidMb - row.DataUsedMb, 0);

    private static int Percent(double part, double whole) =>
        whole > 0 ? (int)Math.Round(part / whole * 100, MidpointRounding.AwayFromZero) : 0;

    private static double Round(double value) => Math.Round(value, 1);
}
