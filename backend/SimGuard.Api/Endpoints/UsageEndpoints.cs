using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Usage;

namespace SimGuard.Api.Endpoints;

// Daily usage per SIM, loaded by ml-service/scripts/load_daily_usage.py.
// Read-only for every role. Contract in backend/docs/api-carriers-usage.md.
public static class UsageEndpoints
{
    private const int DefaultDays = 30;
    private const int MaxDays = 366;

    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder routes)
    {
        var sims = routes.MapGroup("/sims")
            .WithTags("Usage")
            .RequireAuthorization(AuthPolicies.OrgRead);

        sims.MapGet("/{id:int}/usage", GetSimUsage)
            .WithName("GetSimUsage")
            .Produces<SimUsageResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        sims.MapGet("/{id:int}/usage/cycle", GetSimCycleUsage)
            .WithName("GetSimCycleUsage")
            .Produces<SimCycleUsageResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        routes.MapGet("/usage/analytics", GetUsageAnalytics)
            .WithTags("Usage")
            .WithName("GetUsageAnalytics")
            .RequireAuthorization(AuthPolicies.OrgRead)
            .Produces<UsageAnalyticsResponse>()
            .ProducesValidationProblem();

        routes.MapGet("/tiers", GetTiers)
            .WithTags("Usage")
            .WithName("GetTiers")
            .RequireAuthorization(AuthPolicies.OrgRead)
            .Produces<IReadOnlyList<TierAllowanceResponse>>();

        return routes;
    }

    // The billing cycle that holds the fleet's as-of date, with cumulative
    // data, voice and SMS against the SIM's tier allowance.
    private static async Task<IResult> GetSimCycleUsage(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var sim = await db.Sims
            .Where(s => s.Id == id)
            .Select(s => new { s.Tier })
            .FirstOrDefaultAsync(cancellationToken);

        if (sim is null)
        {
            return ApiProblems.NotFound($"SIM {id} was not found.");
        }

        return TypedResults.Ok(await UsageAnalytics.CycleAsync(db, id, sim.Tier, cancellationToken));
    }

    // Fleet usage for the `days` days up to the as-of date (the latest
    // daily_usage date), optionally for one department or one tier.
    private static async Task<IResult> GetUsageAnalytics(
        int? days,
        int? departmentId,
        string? tier,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var window = days ?? 30;

        if (!UsageAnalytics.AllowedDays.Contains(window))
        {
            errors.Add("days", "days must be 7, 30 or 90.");
        }

        var matchedTier = Vocabulary.Match("tier", tier, SimTiers.All, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        return TypedResults.Ok(await UsageAnalytics.AnalyticsAsync(db, window, departmentId, matchedTier, cancellationToken));
    }

    // The tier allowance table, smallest bundle first.
    private static async Task<IResult> GetTiers(
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var tiers = await db.TierAllowances
            .AsNoTracking()
            .OrderBy(t => t.DataMb)
            .Select(TierAllowanceResponse.Projection)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(tiers);
    }

    // One row per day that has data, oldest first. Without "to", the range
    // ends on the SIM's latest usage day (today when it has none); without
    // "from", it covers the 30 days up to "to".
    private static async Task<IResult> GetSimUsage(
        int id,
        DateOnly? from,
        DateOnly? to,
        SimGuardDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        Vocabulary.CheckRange(from, to, errors);

        if (from is not null && to is not null && to.Value.DayNumber - from.Value.DayNumber + 1 > MaxDays)
        {
            errors.Add("to", $"The range must not be longer than {MaxDays} days.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        if (!await db.Sims.AnyAsync(s => s.Id == id, cancellationToken))
        {
            return ApiProblems.NotFound($"SIM {id} was not found.");
        }

        var lastDay = to;

        if (lastDay is null)
        {
            var latest = await db.DailyUsage
                .Where(u => u.SimId == id && (from == null || u.Date >= from))
                .MaxAsync(u => (DateOnly?)u.Date, cancellationToken);

            lastDay = latest ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            // Keep an open-ended range within the limit.
            if (from is not null && lastDay.Value.DayNumber - from.Value.DayNumber + 1 > MaxDays)
            {
                lastDay = from.Value.AddDays(MaxDays - 1);
            }
        }

        var firstDay = from ?? lastDay.Value.AddDays(1 - DefaultDays);

        if (firstDay > lastDay)
        {
            lastDay = firstDay;
        }

        var days = await db.DailyUsage
            .AsNoTracking()
            .Where(u => u.SimId == id && u.Date >= firstDay && u.Date <= lastDay)
            .OrderBy(u => u.Date)
            .Select(DailyUsageItem.Projection)
            .ToListAsync(cancellationToken);

        var totals = new UsageTotals(
            days.Count,
            Math.Round(days.Sum(d => d.TotalMb), 3),
            Math.Round(days.Sum(d => d.BilledMb), 3),
            Math.Round(days.Sum(d => d.VoiceMinutesUsed), 3),
            days.Sum(d => d.SmsCount));

        var asOf = await UsageAnalytics.AsOfAsync(db, cancellationToken);

        return TypedResults.Ok(new SimUsageResponse(id, firstDay, lastDay.Value, totals, days, asOf));
    }
}
