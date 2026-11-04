using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;

namespace SimGuard.Api.Endpoints;

public static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/audit-logs", GetAuditLogs)
            .WithTags("Audit")
            .WithName("GetAuditLogs")
            .RequireAuthorization(AuthPolicies.AuditRead)
            .Produces<PagedResult<AuditLogItem>>()
            .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> GetAuditLogs(
        int? simId,
        string? actorEmail,
        string? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);

        Vocabulary.CheckRange(from, to, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var query = db.AuditLogs.AsNoTracking();

        if (simId is not null)
        {
            query = query.Where(l => l.AffectedSimId == simId);
        }

        var emailPattern = SearchPattern.Exact(actorEmail);

        if (emailPattern is not null)
        {
            query = query.Where(l =>
                l.ActorEmail != null
                && EF.Functions.ILike(l.ActorEmail, emailPattern, SearchPattern.EscapeCharacter));
        }

        var actionValue = action?.Trim();

        if (!string.IsNullOrEmpty(actionValue))
        {
            query = query.Where(l => l.Action == actionValue);
        }

        if (from is not null)
        {
            var fromUtc = from.Value.UtcDateTime;
            query = query.Where(l => l.Timestamp >= fromUtc);
        }

        if (to is not null)
        {
            var toUtc = to.Value.UtcDateTime;
            query = query.Where(l => l.Timestamp <= toUtc);
        }

        var result = await query
            .OrderByDescending(l => l.Timestamp)
            .ThenByDescending(l => l.Id)
            .Select(l => new AuditLogItem(
                l.Id,
                l.Timestamp,
                l.ActorUserId,
                l.ActorEmail,
                l.UserId,
                l.Action,
                l.AffectedSimId,
                l.AffectedSim == null ? null : l.AffectedSim.PhoneNumber,
                l.OldValues,
                l.NewValues))
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }
}
