using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Ml;
using SimGuard.Api.Models;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.Endpoints;

public record ScoringRunStarted(int RunId, bool Merged);

// No SIM ids: a run's scope is reported as a count only.
public record ScoringRunSummary(
    int Id,
    string Trigger,
    string Status,
    bool Force,
    int? ScopeSimCount,
    DateOnly? ScopeFrom,
    DateOnly? ScopeTo,
    int MergedRequests,
    string RequestedBy,
    DateTime RequestedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    int DatesScored,
    int SimDaysScored,
    int SimDaysSkipped,
    int Failures,
    int AlertsCreated,
    int AlertsUpdated,
    string? ErrorSummary,
    string? ModelVersion);

public record MlServiceStatus(bool Configured, bool Reachable, string? Status, string? Detail, string? ModelVersion);

public record ScoringBucketTotals(int Clear, int Escalated, int Flag);

public record ScoringTotals(
    long RiskScores,
    DateOnly? LatestUsageDate,
    DateOnly? LatestScoredDate,
    ScoringBucketTotals? LatestScoredDateBuckets);

public record ScoringStatusResponse(
    bool Enabled,
    string DailyRunTimeNairobi,
    DateTime? NextScheduledRunAt,
    MlServiceStatus MlService,
    ScoringTotals Totals,
    IReadOnlyList<ScoringRunSummary> Runs);

// Scoring controls for the SimGuard operator (Admin only). The status holds
// totals only, never SIM-level data. Contract in backend/docs/scoring-pipeline.md.
public static class AdminScoringEndpoints
{
    private const int RecentRuns = 10;

    public static IEndpointRouteBuilder MapAdminScoringEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/scoring")
            .WithTags("Scoring")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapPost("/run", RunNow)
            .WithName("RunScoringNow")
            .Produces<ScoringRunStarted>(StatusCodes.Status202Accepted);

        group.MapGet("/status", GetStatus)
            .WithName("GetScoringStatus")
            .Produces<ScoringStatusResponse>();

        return routes;
    }

    // Queues a manual run of every scorable SIM-day not yet scored and
    // returns at once. A request while a run is queued is merged into it.
    private static async Task<IResult> RunNow(
        HttpContext http,
        ScoringQueue queue,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var email = http.User.FindFirst(TokenService.EmailClaim)?.Value ?? "unknown";
        var result = await queue.EnqueueAsync(
            new ScoringRequest(ScoringTriggers.Manual, false, null, null, null, TokenService.GetUserId(http.User), email),
            cancellationToken);

        audit.Add(AuditActions.ScoringRunRequested, null, null, new { runId = result.RunId, merged = result.Merged });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Accepted("/api/admin/scoring/status", new ScoringRunStarted(result.RunId, result.Merged));
    }

    private static async Task<IResult> GetStatus(
        SimGuardDbContext db,
        MlServiceClient ml,
        IOptions<MlServiceOptions> mlOptions,
        IOptionsMonitor<ScoringOptions> scoringOptions,
        CancellationToken cancellationToken)
    {
        var options = scoringOptions.CurrentValue;
        var runTime = NairobiClock.ParseRunTime(options.DailyRunTimeNairobi);

        var health = await ml.GetHealthAsync(cancellationToken);
        string? modelVersion = null;

        if (health.Reachable)
        {
            try
            {
                modelVersion = (await ml.GetVersionAsync(cancellationToken)).ModelVersion();
            }
            catch (MlServiceException)
            {
                // Reported through health.
            }
        }

        var latestUsage = await db.DailyUsage.MaxAsync(u => (DateOnly?)u.Date, cancellationToken);
        var latestScored = await db.RiskScores.MaxAsync(r => (DateOnly?)r.Date, cancellationToken);
        var riskScores = await db.RiskScores.LongCountAsync(cancellationToken);
        ScoringBucketTotals? buckets = null;

        if (latestScored is { } last)
        {
            var counts = await db.RiskScores
                .Where(r => r.Date == last)
                .GroupBy(r => r.Bucket)
                .Select(g => new { Bucket = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            int Count(string bucket) => counts.FirstOrDefault(c => c.Bucket == bucket)?.Count ?? 0;
            buckets = new ScoringBucketTotals(Count(RiskBuckets.Clear), Count(RiskBuckets.Escalated), Count(RiskBuckets.Flag));
        }

        var runs = (await db.ScoringRuns.AsNoTracking()
                .OrderByDescending(r => r.Id)
                .Take(RecentRuns)
                .ToListAsync(cancellationToken))
            .Select(Summary)
            .ToList();

        return TypedResults.Ok(new ScoringStatusResponse(
            options.Enabled,
            runTime.ToString("HH:mm"),
            options.Enabled ? NairobiClock.NextRunUtc(Timestamps.UtcNow(), runTime) : null,
            new MlServiceStatus(
                !string.IsNullOrWhiteSpace(mlOptions.Value.BaseUrl),
                health.Reachable,
                health.Status,
                health.Detail,
                modelVersion),
            new ScoringTotals(riskScores, latestUsage, latestScored, buckets),
            runs));
    }

    internal static ScoringRunSummary Summary(ScoringRun r) => new(
        r.Id, r.Trigger, r.Status, r.Force, r.ScopeSimIds?.Length, r.ScopeFrom, r.ScopeTo, r.MergedRequests,
        r.RequestedBy, r.RequestedAt, r.StartedAt, r.FinishedAt, r.FirstDate, r.LastDate, r.DatesScored,
        r.SimDaysScored, r.SimDaysSkipped, r.Failures, r.AlertsCreated, r.AlertsUpdated, r.ErrorSummary,
        r.ModelVersion);
}
