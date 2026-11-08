using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Email;
using SimGuard.Api.Ml;
using SimGuard.Api.Models;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.Endpoints;

public record ApiHealth(bool Up, string Version, DateTime StartedAt, long UptimeSeconds);

public record DatabaseHealth(bool Ok, long? ResponseMs);

public record MlHealthSummary(bool Configured, bool Reachable, string? ModelVersion);

public record LastScoringRun(
    int Id,
    string Trigger,
    string Status,
    DateTime RequestedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    double? DurationSeconds,
    string? FailureReason);

public record ScoringHealth(bool Enabled, LastScoringRun? LastRun, DateTime? NextScheduledRunAt);

public record EmailHealth(bool Configured);

public record UserRoleCount(string Role, int Active, int Disabled);

public record AccessRequestCount(string Status, int Count);

public record DailySignIns(DateOnly Date, int Succeeded, int Failed);

public record DailyScoringRuns(DateOnly Date, int Succeeded, int Failed);

public record SystemStatusResponse(
    DateTime GeneratedAt,
    ApiHealth Api,
    DatabaseHealth Database,
    MlHealthSummary MlService,
    ScoringHealth Scoring,
    EmailHealth Email,
    IReadOnlyList<UserRoleCount> UsersByRole,
    IReadOnlyList<AccessRequestCount> AccessRequests,
    IReadOnlyList<DailySignIns> SignInsPerDay,
    IReadOnlyList<DailyScoringRuns> ScoringRunsPerDay);

// The Admin's System health screen. Read only, counts and flags only: no
// organisation data, no secret, key or connection string. Contract in
// backend/docs/auth.md ("System status").
public static class SystemStatusEndpoints
{
    public const int Days = 14;
    private const int FailureReasonMaxLength = 300;

    public static IEndpointRouteBuilder MapSystemStatusEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/admin/system-status", GetSystemStatus)
            .WithName("GetSystemStatus")
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .Produces<SystemStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return routes;
    }

    private static async Task<IResult> GetSystemStatus(
        SimGuardDbContext db,
        MlServiceClient ml,
        IOptions<MlServiceOptions> mlOptions,
        IOptions<BrevoOptions> brevoOptions,
        IOptionsMonitor<ScoringOptions> scoringOptions,
        CancellationToken cancellationToken)
    {
        var now = Timestamps.UtcNow();

        // Database: one trivial query, timed. Any failure is "down", never an error response.
        var database = await CheckDatabaseAsync(db, cancellationToken);

        // ML service: health and model version. Failures are reported as not reachable.
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
                // The model version stays empty.
            }
        }

        var started = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var version = typeof(SystemStatusEndpoints).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+')[0]
            ?? "unknown";

        if (!database.Ok)
        {
            // Without the database nothing else can be read.
            return TypedResults.Ok(new SystemStatusResponse(
                now,
                new ApiHealth(true, version, started, (long)(now - started).TotalSeconds),
                database,
                new MlHealthSummary(!string.IsNullOrWhiteSpace(mlOptions.Value.BaseUrl), health.Reachable, modelVersion),
                new ScoringHealth(scoringOptions.CurrentValue.Enabled, null, NextRun(scoringOptions.CurrentValue, now)),
                new EmailHealth(!string.IsNullOrWhiteSpace(brevoOptions.Value.ApiKey)),
                [], [], [], []));
        }

        var options = scoringOptions.CurrentValue;
        var lastRun = await db.ScoringRuns.AsNoTracking()
            .Where(r => r.Status != ScoringRunStatuses.Queued && r.Status != ScoringRunStatuses.Running)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var today = DateOnly.FromDateTime(now);
        var firstDay = today.AddDays(-(Days - 1));
        var since = firstDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return TypedResults.Ok(new SystemStatusResponse(
            now,
            new ApiHealth(true, version, started, (long)(now - started).TotalSeconds),
            database,
            new MlHealthSummary(!string.IsNullOrWhiteSpace(mlOptions.Value.BaseUrl), health.Reachable, modelVersion),
            new ScoringHealth(options.Enabled, lastRun is null ? null : ToLastRun(lastRun), NextRun(options, now)),
            new EmailHealth(!string.IsNullOrWhiteSpace(brevoOptions.Value.ApiKey)),
            await UsersByRoleAsync(db, cancellationToken),
            await AccessRequestsAsync(db, cancellationToken),
            await SignInsAsync(db, firstDay, since, cancellationToken),
            await ScoringRunsAsync(db, firstDay, since, cancellationToken)));
    }

    private static DateTime? NextRun(ScoringOptions options, DateTime now) =>
        options.Enabled
            ? NairobiClock.NextRunUtc(now, NairobiClock.ParseRunTime(options.DailyRunTimeNairobi))
            : null;

    private static async Task<DatabaseHealth> CheckDatabaseAsync(SimGuardDbContext db, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();

        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT 1", cancellationToken);
            return new DatabaseHealth(true, timer.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DatabaseHealth(false, null);
        }
    }

    private static LastScoringRun ToLastRun(ScoringRun run)
    {
        var duration = run.StartedAt is { } begun && run.FinishedAt is { } end
            ? Math.Round((end - begun).TotalSeconds, 1)
            : (double?)null;
        var reason = run.ErrorSummary;

        if (reason is { Length: > FailureReasonMaxLength })
        {
            reason = reason[..FailureReasonMaxLength];
        }

        return new LastScoringRun(
            run.Id, run.Trigger, run.Status, run.RequestedAt, run.StartedAt, run.FinishedAt, duration, reason);
    }

    private static async Task<IReadOnlyList<UserRoleCount>> UsersByRoleAsync(
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from u in db.Users.AsNoTracking()
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                group u by new { Role = r.Name, u.IsDisabled } into g
                select new { g.Key.Role, g.Key.IsDisabled, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return AppRoles.All
            .Select(role => new UserRoleCount(
                role,
                rows.Where(r => r.Role == role && !r.IsDisabled).Sum(r => r.Count),
                rows.Where(r => r.Role == role && r.IsDisabled).Sum(r => r.Count)))
            .ToList();
    }

    private static async Task<IReadOnlyList<AccessRequestCount>> AccessRequestsAsync(
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var rows = await db.AccessRequests.AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return AccessRequestStatuses.All
            .Select(status => new AccessRequestCount(status, rows.FirstOrDefault(r => r.Status == status)?.Count ?? 0))
            .ToList();
    }

    // Days are UTC dates; a day with no events is 0.
    private static async Task<IReadOnlyList<DailySignIns>> SignInsAsync(
        SimGuardDbContext db,
        DateOnly firstDay,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var rows = await db.AuthEvents.AsNoTracking()
            .Where(e => e.CreatedAt >= since
                && (e.EventType == AuthEventTypes.LoginSucceeded || e.EventType == AuthEventTypes.LoginFailed))
            .GroupBy(e => new { e.CreatedAt.Date, e.EventType })
            .Select(g => new { g.Key.Date, g.Key.EventType, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return Enumerable.Range(0, Days)
            .Select(offset =>
            {
                var day = firstDay.AddDays(offset);
                int Count(string type) => rows
                    .Where(r => DateOnly.FromDateTime(r.Date) == day && r.EventType == type)
                    .Sum(r => r.Count);
                return new DailySignIns(day, Count(AuthEventTypes.LoginSucceeded), Count(AuthEventTypes.LoginFailed));
            })
            .ToList();
    }

    // Runs by the day they were requested. "Failed" includes partial runs.
    private static async Task<IReadOnlyList<DailyScoringRuns>> ScoringRunsAsync(
        SimGuardDbContext db,
        DateOnly firstDay,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var rows = await db.ScoringRuns.AsNoTracking()
            .Where(r => r.RequestedAt >= since)
            .GroupBy(r => new { r.RequestedAt.Date, r.Status })
            .Select(g => new { g.Key.Date, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return Enumerable.Range(0, Days)
            .Select(offset =>
            {
                var day = firstDay.AddDays(offset);
                int Count(Func<string, bool> match) => rows
                    .Where(r => DateOnly.FromDateTime(r.Date) == day && match(r.Status))
                    .Sum(r => r.Count);
                return new DailyScoringRuns(
                    day,
                    Count(status => status == ScoringRunStatuses.Succeeded),
                    Count(status => status == ScoringRunStatuses.Failed || status == ScoringRunStatuses.Partial));
            })
            .ToList();
    }
}
