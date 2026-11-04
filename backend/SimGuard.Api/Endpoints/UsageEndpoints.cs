using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;

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

        return routes;
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

        return TypedResults.Ok(new SimUsageResponse(id, firstDay, lastDay.Value, totals, days));
    }
}
