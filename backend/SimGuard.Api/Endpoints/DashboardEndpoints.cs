using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.TierRecommendations;
using SimGuard.Api.Usage;

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
            .RequireAuthorization(AuthPolicies.OrgRead)
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

        // The window ends on the as-of date, the latest daily_usage date, so
        // the demo data (which ends on 2025-03-31) reads as current. Without
        // usage it falls back to the clock.
        var asOf = await UsageAnalytics.AsOfAsync(db, cancellationToken);
        DateTime from;
        DateTime to;

        if (asOf is not null)
        {
            from = asOf.Value.AddDays(1 - window).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            to = asOf.Value.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
        }
        else
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            to = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
            from = to.AddDays(-window);
        }

        var fromDate = DateOnly.FromDateTime(from);
        var toDate = DateOnly.FromDateTime(to);

        var activeSims = await db.Sims
            .CountAsync(s => s.Status != SimStatuses.Inactive, cancellationToken);

        var flagged = await db.Alerts
            .CountAsync(a => a.Status == AlertStatuses.Flagged, cancellationToken);

        var toReview = await db.Alerts
            .CountAsync(a => a.Status == AlertStatuses.Escalated, cancellationToken);

        // Alerts in the window by the day they were detected; alerts stored
        // before scoring existed have no detectedOn and fall back to their
        // stored time.
        var inWindow = db.Alerts.Where(a =>
            a.DetectedOn != null
                ? a.DetectedOn >= fromDate && a.DetectedOn <= toDate
                : a.CreatedAt >= from && a.CreatedAt <= to);

        var resolved = await inWindow
            .CountAsync(a => a.Status == AlertStatuses.Cleared, cancellationToken);

        var counts = await inWindow
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

        var tierChanges = await db.TierRecommendations
            .Where(r => r.Status == TierRecommendationStatuses.Open)
            .GroupBy(r => r.Recommendation)
            .OrderBy(g => g.Key)
            .Select(g => new TierChangeCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        var (dataWaste, _) = await UsageAnalytics.DataWasteAsync(db, window, cancellationToken);

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
            tierChanges,
            dataWaste,
            asOf));
    }
}
