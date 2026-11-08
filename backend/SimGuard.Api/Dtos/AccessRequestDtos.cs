using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

// Body of POST /api/access-requests.
public record CreateAccessRequestRequest(
    string? Name,
    string? Email,
    string? Organisation,
    string? Role,
    string? Note);

// Body of PATCH /api/access-requests/{id}/status: approved or declined.
public record ReviewAccessRequestRequest(string? Status);

// Response of POST /api/access-requests. Always the same, so it does not
// reveal whether the email already has a request or an account.
public record AccessRequestReceivedResponse(bool Received);

public record AccessRequestResponse(
    int Id,
    string Name,
    string Email,
    string Organisation,
    string Role,
    string? Note,
    string Status,
    DateTime CreatedAt,
    DateTime? ReviewedAt,
    string? ReviewedByEmail)
{
    public static readonly Expression<Func<AccessRequest, AccessRequestResponse>> Projection =
        r => new AccessRequestResponse(
            r.Id, r.Name, r.Email, r.Organisation, r.Role, r.Note, r.Status,
            r.CreatedAt, r.ReviewedAt, r.ReviewedByEmail);
}
