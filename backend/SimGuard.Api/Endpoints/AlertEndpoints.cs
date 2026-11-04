using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

public static class AlertEndpoints
{
    private const int RecentAlertCount = 5;

    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/alerts")
            .WithTags("Alerts")
            .RequireAuthorization(AuthPolicies.OrgRead);

        group.MapGet("/", GetAlerts)
            .WithName("GetAlerts")
            .Produces<PagedResult<AlertListItem>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:int}", GetAlert)
            .WithName("GetAlert")
            .Produces<AlertDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:int}/status", UpdateAlertStatus)
            .WithName("UpdateAlertStatus")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<AlertDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> GetAlerts(
        string? status,
        string? type,
        int? simId,
        decimal? minScore,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? sort,
        string? order,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);
        var statusValue = Vocabulary.Match("status", status, AlertStatuses.All, errors);
        var typeValue = Vocabulary.Match("type", type, AlertTypes.All, errors);
        var sortValue = Vocabulary.Match("sort", sort, ["createdAt", "riskScore"], errors) ?? "createdAt";
        var orderValue = Vocabulary.Match("order", order, ["desc", "asc"], errors) ?? "desc";

        if (minScore is < 0 or > 1)
        {
            errors.Add("minScore", "minScore must be between 0 and 1.");
        }

        Vocabulary.CheckRange(from, to, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var query = db.Alerts.AsNoTracking();

        if (statusValue is not null)
        {
            query = query.Where(a => a.Status == statusValue);
        }

        if (typeValue is not null)
        {
            query = query.Where(a => a.Type == typeValue);
        }

        if (simId is not null)
        {
            query = query.Where(a => a.SimId == simId);
        }

        if (minScore is not null)
        {
            query = query.Where(a => a.RiskScore >= minScore);
        }

        if (from is not null)
        {
            var fromUtc = from.Value.UtcDateTime;
            query = query.Where(a => a.CreatedAt >= fromUtc);
        }

        if (to is not null)
        {
            var toUtc = to.Value.UtcDateTime;
            query = query.Where(a => a.CreatedAt <= toUtc);
        }

        var ascending = orderValue == "asc";

        var ordered = (sortValue, ascending) switch
        {
            ("riskScore", true) => query.OrderBy(a => a.RiskScore).ThenBy(a => a.Id),
            ("riskScore", false) => query.OrderByDescending(a => a.RiskScore).ThenByDescending(a => a.Id),
            (_, true) => query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id),
            _ => query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
        };

        var result = await ordered
            .Select(AlertListItem.Projection)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetAlert(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var alert = await FindAlertDetailAsync(db, id, cancellationToken);

        return alert is null
            ? ApiProblems.NotFound($"Alert {id} was not found.")
            : TypedResults.Ok(alert);
    }

    private static async Task<IResult> UpdateAlertStatus(
        int id,
        AlertStatusRequest request,
        SimGuardDbContext db,
        AuditWriter audit,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            errors.Add("status", "status is required.");
        }

        var status = Vocabulary.Match("status", request.Status, AlertStatuses.All, errors);
        var resolution = Vocabulary.Match("resolution", request.Resolution, AlertResolutions.All, errors);
        var note = string.IsNullOrWhiteSpace(request.ResolutionNote) ? null : request.ResolutionNote.Trim();

        if (note is { Length: > AlertResolutions.MaxNoteLength })
        {
            errors.Add(
                "resolutionNote",
                $"resolutionNote must be at most {AlertResolutions.MaxNoteLength} characters.");
        }

        if (status == AlertStatuses.Cleared && resolution is null && string.IsNullOrWhiteSpace(request.Resolution))
        {
            errors.Add(
                "resolution",
                $"resolution is required when clearing an alert: {string.Join(", ", AlertResolutions.All)}.");
        }

        if (status is not null && status != AlertStatuses.Cleared)
        {
            if (!string.IsNullOrWhiteSpace(request.Resolution))
            {
                errors.Add("resolution", "resolution can only be sent when clearing an alert.");
            }

            if (note is not null)
            {
                errors.Add("resolutionNote", "resolutionNote can only be sent when clearing an alert.");
            }
        }

        if (errors.HasErrors || status is null)
        {
            return errors.ToProblem();
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Lock the row so two reviewers cannot both move the same alert.
            var alerts = await db.Alerts
                .FromSql($"SELECT * FROM alerts WHERE id = {id} FOR UPDATE")
                .ToListAsync(cancellationToken);

            var alert = alerts.SingleOrDefault();

            if (alert is null)
            {
                return ApiProblems.NotFound($"Alert {id} was not found.");
            }

            if (!AlertStatuses.CanMove(alert.Status, status))
            {
                return ApiProblems.Conflict(
                    $"Alert {id} is '{alert.Status}' and cannot move to '{status}'. "
                    + "Allowed: flagged or escalated to cleared, cleared to escalated, escalated to flagged.");
            }

            var before = Snapshot(alert);

            alert.Status = status;

            if (status == AlertStatuses.Cleared)
            {
                alert.ResolvedAt = Timestamps.UtcNow();
                alert.ResolvedByUserId = TokenService.GetUserId(httpContext.User);
                alert.Resolution = resolution;
                alert.ResolutionNote = note;
            }
            else
            {
                alert.ResolvedAt = null;
                alert.ResolvedByUserId = null;
                alert.Resolution = null;
                alert.ResolutionNote = null;
            }

            audit.Add(AuditActions.AlertStatusChanged, alert.SimId, before, Snapshot(alert));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return TypedResults.Ok(await FindAlertDetailAsync(db, id, cancellationToken));
    }

    internal static async Task<AlertDetailResponse?> FindAlertDetailAsync(
        SimGuardDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        var detail = await db.Alerts
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AlertDetailResponse(
                a.Id,
                a.SimId,
                a.Sim.PhoneNumber,
                a.Type,
                a.RiskScore,
                a.RiskLevel,
                a.Status,
                a.CreatedAt,
                a.ResolvedAt,
                a.Reasons,
                a.ModelVersion,
                a.Resolution,
                a.ResolutionNote,
                a.ResolvedByUser == null
                    ? null
                    : new UserReference(a.ResolvedByUser.Id, a.ResolvedByUser.Name, a.ResolvedByUser.Email),
                new AlertSimSummary(
                    a.Sim.Id,
                    a.Sim.PhoneNumber,
                    a.Sim.Iccid,
                    a.Sim.Carrier.Name,
                    a.Sim.PlanType,
                    a.Sim.Status),
                a.Sim.Allocations
                    .Where(x => x.ReturnDate == null)
                    .OrderByDescending(x => x.AssignmentDate)
                    .Select(x => new CurrentAllocationResponse(
                        x.Id,
                        x.EmployeeId,
                        x.Employee.Name,
                        x.Employee.Department.Name,
                        x.AssignmentDate))
                    .FirstOrDefault(),
                Array.Empty<RecentAlertItem>()))
            .FirstOrDefaultAsync(cancellationToken);

        if (detail is null)
        {
            return null;
        }

        var recent = await db.Alerts
            .AsNoTracking()
            .Where(a => a.SimId == detail.SimId && a.Id != id)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(RecentAlertCount)
            .Select(a => new RecentAlertItem(
                a.Id,
                a.Type,
                a.RiskScore,
                a.RiskLevel,
                a.Status,
                a.CreatedAt,
                a.ResolvedAt))
            .ToListAsync(cancellationToken);

        return detail with { RecentAlerts = recent };
    }

    private static object Snapshot(Alert alert) => new
    {
        alertId = alert.Id,
        status = alert.Status,
        resolution = alert.Resolution,
        resolutionNote = alert.ResolutionNote,
        resolvedAt = alert.ResolvedAt,
        resolvedByUserId = alert.ResolvedByUserId
    };
}

// Query-string helpers shared by the alerts, risk and audit endpoints.
internal static class Vocabulary
{
    // Returns the canonical spelling of value (case-insensitive), null when
    // value is empty, and records an error when it is not allowed.
    public static string? Match(
        string field,
        string? value,
        IEnumerable<string> allowed,
        ValidationErrors errors)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var options = allowed.ToList();
        var match = options.FirstOrDefault(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            errors.Add(field, $"{field} must be one of: {string.Join(", ", options)}.");
        }

        return match;
    }

    public static void CheckRange<T>(T? from, T? to, ValidationErrors errors)
        where T : struct, IComparable<T>
    {
        if (from is not null && to is not null && from.Value.CompareTo(to.Value) > 0)
        {
            errors.Add("from", "from must not be after to.");
        }
    }

    // Pages an already sorted in-memory list the same way as ToPagedResultAsync.
    public static PagedResult<T> Page<T>(IReadOnlyList<T> items, PageRequest paging)
    {
        var skip = (long)(paging.Page - 1) * paging.PageSize;

        var pageItems = skip >= items.Count
            ? []
            : items.Skip((int)skip).Take(paging.PageSize).ToList();

        return new PagedResult<T>(pageItems, paging.Page, paging.PageSize, items.Count);
    }
}
