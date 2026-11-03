using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;

namespace SimGuard.Api.Endpoints;

public static class DashboardEndpoints
{
    private const int NeedsReviewCount = 5;
    private static readonly int[] AllowedDays = [7, 30, 90];

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/dashboard/summary", GetSummary)
            .WithTags("Dashboard")
            .WithName("GetDashboardSummary")
            .RequireAuthorization(AuthPolicies.CanRead)
            .Produces<DashboardSummaryResponse>()
            .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> GetSummary(
        int? days,
        SimGuardDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var window = days ?? AllowedDays[0];

        if (!AllowedDays.Contains(window))
        {
            var errors = new ValidationErrors();
            errors.Add("days", "days must be 7, 30 or 90.");
            return errors.ToProblem();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var to = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var from = to.AddDays(-window);

        var activeSims = await db.Sims
            .CountAsync(s => s.Status != SimStatuses.Inactive, cancellationToken);

        var flagged = await db.Alerts
            .CountAsync(a => a.Status == AlertStatuses.Flagged, cancellationToken);

        var toReview = await db.Alerts
            .CountAsync(a => a.Status == AlertStatuses.Escalated, cancellationToken);

        var resolved = await db.Alerts
            .CountAsync(
                a => a.Status == AlertStatuses.Cleared && a.ResolvedAt >= from && a.ResolvedAt <= to,
                cancellationToken);

        var counts = await db.Alerts
            .Where(a => a.CreatedAt >= from && a.CreatedAt <= to)
            .GroupBy(a => a.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var alertsByType = AlertTypes.All.ToDictionary(
            t => t,
            t => counts.FirstOrDefault(c => c.Type == t)?.Count ?? 0);

        var needsReview = await db.Alerts
            .AsNoTracking()
            .Where(a => a.Status == AlertStatuses.Flagged || a.Status == AlertStatuses.Escalated)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(NeedsReviewCount)
            .Select(AlertListItem.Projection)
            .ToListAsync(cancellationToken);

        // tierChanges and dataWaste need usage ingestion (later issues).
        return TypedResults.Ok(new DashboardSummaryResponse(
            window,
            from,
            to,
            activeSims,
            flagged,
            toReview,
            resolved,
            alertsByType,
            needsReview,
            [],
            null));
    }
}
