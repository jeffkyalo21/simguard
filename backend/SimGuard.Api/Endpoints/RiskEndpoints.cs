using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;

namespace SimGuard.Api.Endpoints;

// Risk scores and the SIM activity timeline. Read-only for every role.
public static class RiskEndpoints
{
    private const int DefaultHistoryDays = 90;
    private const int MaxHistoryDays = 365;

    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder routes)
    {
        var sims = routes.MapGroup("/sims")
            .WithTags("Risk")
            .RequireAuthorization(AuthPolicies.OrgRead);

        sims.MapGet("/{id:int}/risk", GetSimRisk)
            .WithName("GetSimRisk")
            .Produces<SimRiskResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        sims.MapGet("/{id:int}/activity", GetSimActivity)
            .WithName("GetSimActivity")
            .Produces<PagedResult<ActivityItem>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        routes.MapGet("/risk-scores", GetRiskScores)
            .WithTags("Risk")
            .WithName("GetRiskScores")
            .RequireAuthorization(AuthPolicies.OrgRead)
            .Produces<PagedResult<RiskScoreItem>>()
            .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> GetSimRisk(
        int id,
        int? days,
        DateOnly? to,
        SimGuardDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var window = days ?? DefaultHistoryDays;

        if (window is < 1 or > MaxHistoryDays)
        {
            var errors = new ValidationErrors();
            errors.Add("days", $"days must be between 1 and {MaxHistoryDays}.");
            return errors.ToProblem();
        }

        if (!await db.Sims.AnyAsync(s => s.Id == id, cancellationToken))
        {
            return ApiProblems.NotFound($"SIM {id} was not found.");
        }

        // `to` lets the screen end the history on the fleet's as-of date
        // instead of the clock (the demo data ends on 2025-03-31).
        var today = to ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var firstDay = today.AddDays(1 - window);

        var latest = await db.RiskScores
            .AsNoTracking()
            .Where(r => r.SimId == id)
            .OrderByDescending(r => r.Date)
            .Select(r => new RiskLatest(
                r.Date,
                r.Probability,
                r.Bucket,
                r.Bucket == RiskBuckets.Flag
                    ? RiskLevels.High
                    : r.Bucket == RiskBuckets.Escalated ? RiskLevels.Medium : RiskLevels.Low,
                r.Reasons,
                r.ModelVersion,
                r.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        var history = await db.RiskScores
            .AsNoTracking()
            .Where(r => r.SimId == id && r.Date >= firstDay && r.Date <= today)
            .OrderBy(r => r.Date)
            .Select(r => new RiskHistoryPoint(r.Date, r.Probability, r.Bucket))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new SimRiskResponse(id, window, latest, history));
    }

    private static async Task<IResult> GetRiskScores(
        int? simId,
        DateOnly? from,
        DateOnly? to,
        string? bucket,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);
        var bucketValue = Vocabulary.Match("bucket", bucket, RiskBuckets.All, errors);

        Vocabulary.CheckRange(from, to, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var query = db.RiskScores.AsNoTracking();

        if (simId is not null)
        {
            query = query.Where(r => r.SimId == simId);
        }

        if (from is not null)
        {
            query = query.Where(r => r.Date >= from);
        }

        if (to is not null)
        {
            query = query.Where(r => r.Date <= to);
        }

        if (bucketValue is not null)
        {
            query = query.Where(r => r.Bucket == bucketValue);
        }

        var result = await query
            .OrderByDescending(r => r.Date)
            .ThenBy(r => r.SimId)
            .Select(RiskScoreItem.Projection)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    // Merges alerts, allocations and audit rows for one SIM. A single SIM
    // has a bounded history, so the merge and paging happen in memory.
    private static async Task<IResult> GetSimActivity(
        int id,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        if (!await db.Sims.AnyAsync(s => s.Id == id, cancellationToken))
        {
            return ApiProblems.NotFound($"SIM {id} was not found.");
        }

        var alerts = await db.Alerts
            .AsNoTracking()
            .Where(a => a.SimId == id)
            .Select(a => new
            {
                a.Id,
                a.Type,
                a.RiskScore,
                a.Status,
                a.CreatedAt,
                a.ResolvedAt,
                a.Resolution,
                ResolvedByEmail = a.ResolvedByUser == null ? null : a.ResolvedByUser.Email
            })
            .ToListAsync(cancellationToken);

        var allocations = await db.Allocations
            .AsNoTracking()
            .Where(a => a.SimId == id)
            .Select(a => new
            {
                a.Id,
                a.AssignmentDate,
                a.ReturnDate,
                EmployeeName = a.Employee.Name,
                DepartmentName = a.Employee.Department.Name,
                AssignedByName = a.AssignedByEmployee.Name
            })
            .ToListAsync(cancellationToken);

        var auditRows = await db.AuditLogs
            .AsNoTracking()
            .Where(l => l.AffectedSimId == id
                && !l.Action.StartsWith("alert.")
                && !l.Action.StartsWith("allocation."))
            .Select(l => new { l.Id, l.Action, l.Timestamp, l.ActorEmail })
            .ToListAsync(cancellationToken);

        var items = new List<ActivityItem>();

        foreach (var a in alerts)
        {
            items.Add(new ActivityItem(
                "alertOpened",
                a.CreatedAt,
                $"{a.Type} alert opened (risk score {a.RiskScore:0.00})",
                null,
                null,
                "alert",
                a.Id));

            if (a.Status == AlertStatuses.Cleared && a.ResolvedAt is { } resolvedAt)
            {
                items.Add(new ActivityItem(
                    "alertCleared",
                    resolvedAt,
                    $"{a.Type} alert cleared ({a.Resolution})",
                    a.ResolvedByEmail,
                    null,
                    "alert",
                    a.Id));
            }
        }

        foreach (var a in allocations)
        {
            items.Add(new ActivityItem(
                "allocationStarted",
                a.AssignmentDate,
                $"Assigned to {a.EmployeeName} ({a.DepartmentName}) by {a.AssignedByName}",
                null,
                null,
                "allocation",
                a.Id));

            if (a.ReturnDate is { } returnDate)
            {
                items.Add(new ActivityItem(
                    "allocationEnded",
                    returnDate,
                    $"Returned by {a.EmployeeName}",
                    null,
                    null,
                    "allocation",
                    a.Id));
            }
        }

        foreach (var l in auditRows)
        {
            items.Add(new ActivityItem("audit", l.Timestamp, l.Action, l.ActorEmail, l.Action, "auditLog", l.Id));
        }

        var sorted = items
            .OrderByDescending(i => i.OccurredAt)
            .ThenByDescending(i => i.ReferenceId)
            .ToList();

        return TypedResults.Ok(Vocabulary.Page(sorted, paging));
    }
}
