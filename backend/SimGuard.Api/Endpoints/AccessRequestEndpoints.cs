using Microsoft.EntityFrameworkCore;
using Npgsql;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

// Requests for an account from the landing page. Contract in
// backend/docs/access-requests.md.
public static class AccessRequestEndpoints
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 256;
    public const int OrganisationMaxLength = 150;
    public const int NoteMaxLength = 500;

    // Created by the AddAccessRequests migration: one pending request per email (stored in lower case).
    private const string PendingEmailIndex = "idx_access_requests_pending_email";

    public static readonly IReadOnlyList<string> RequestableRoles = [AppRoles.SecurityOfficer, AppRoles.Auditor];

    public static IEndpointRouteBuilder MapAccessRequestEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/access-requests")
            .WithTags("Access requests");

        group.MapPost("/", CreateAccessRequest)
            .WithName("CreateAccessRequest")
            .AllowAnonymous()
            .RequireRateLimiting(SecurityServiceExtensions.AccessRequestRateLimitPolicy)
            .Produces<AccessRequestReceivedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/", GetAccessRequests)
            .WithName("GetAccessRequests")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .Produces<PagedResult<AccessRequestResponse>>()
            .ProducesValidationProblem();

        group.MapPatch("/{id:int}/status", ReviewAccessRequest)
            .WithName("ReviewAccessRequest")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .Produces<AccessRequestResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> CreateAccessRequest(
        CreateAccessRequestRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = errors.RequiredString("name", request.Name, NameMaxLength);
        var email = errors.RequiredEmail("email", request.Email, EmailMaxLength).ToLowerInvariant();
        var organisation = errors.RequiredString("organisation", request.Organisation, OrganisationMaxLength);
        var role = RequestableRoles.FirstOrDefault(r =>
            string.Equals(r, request.Role?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (role is null)
        {
            errors.Add("role", $"role must be one of: {string.Join(", ", RequestableRoles)}.");
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        if (note is not null && note.Length > NoteMaxLength)
        {
            errors.Add("note", $"note must be at most {NoteMaxLength} characters.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var received = TypedResults.Accepted((string?)null, new AccessRequestReceivedResponse(true));

        // A second pending request from the same email is accepted but not
        // stored again, and the answer is the same: the response must not
        // reveal what exists. The Admin already has the first one.
        if (await db.AccessRequests.AnyAsync(
                r => r.Status == AccessRequestStatuses.Pending && r.Email == email,
                cancellationToken))
        {
            return received;
        }

        db.AccessRequests.Add(new AccessRequest
        {
            Name = name,
            Email = email,
            Organisation = organisation,
            Role = role!,
            Note = note,
            Status = AccessRequestStatuses.Pending,
            CreatedAt = Timestamps.UtcNow()
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: PendingEmailIndex })
        {
            // Two identical submissions raced; one request is stored.
        }

        return received;
    }

    private static async Task<IResult> GetAccessRequests(
        string? status,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);
        var statusFilter = status?.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(statusFilter) && !AccessRequestStatuses.All.Contains(statusFilter))
        {
            errors.Add("status", $"status must be one of: {string.Join(", ", AccessRequestStatuses.All)}.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var query = db.AccessRequests.AsNoTracking();

        if (!string.IsNullOrEmpty(statusFilter))
        {
            query = query.Where(r => r.Status == statusFilter);
        }

        var result = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Select(AccessRequestResponse.Projection)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> ReviewAccessRequest(
        int id,
        ReviewAccessRequestRequest request,
        HttpContext httpContext,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var newStatus = request.Status?.Trim().ToLowerInvariant();

        if (newStatus is not (AccessRequestStatuses.Approved or AccessRequestStatuses.Declined))
        {
            var errors = new ValidationErrors();
            errors.Add("status", "status must be approved or declined.");
            return errors.ToProblem();
        }

        var item = await db.AccessRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (item is null)
        {
            return ApiProblems.NotFound($"Access request {id} was not found.");
        }

        if (item.Status != AccessRequestStatuses.Pending)
        {
            return ApiProblems.Conflict($"Access request {id} is already {item.Status}.");
        }

        var oldStatus = item.Status;
        item.Status = newStatus;
        item.ReviewedAt = Timestamps.UtcNow();
        item.ReviewedByEmail = httpContext.User.FindFirst(TokenService.EmailClaim)?.Value?.Trim();

        audit.Add(
            AuditActions.AccessRequestReviewed,
            null,
            new { accessRequestId = id, status = oldStatus },
            new { accessRequestId = id, status = newStatus, email = item.Email, role = item.Role });

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await db.AccessRequests.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(AccessRequestResponse.Projection)
            .SingleAsync(cancellationToken));
    }
}
