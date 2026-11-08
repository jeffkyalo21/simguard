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
// Approving creates the account with a generated password that is emailed to
// the person. TemporaryPassword is only accepted in Development (test
// scripts) and replaces the generated one. It is never stored or audited.
public record ReviewAccessRequestRequest(string? Status, string? TemporaryPassword = null);

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
    string? ReviewedByEmail,
    bool? EmailSent,
    // Set by the review call only: true when approving created the account,
    // false when an account with that email already existed.
    bool? AccountCreated = null)
{
    public static readonly Expression<Func<AccessRequest, AccessRequestResponse>> Projection =
        r => new AccessRequestResponse(
            r.Id, r.Name, r.Email, r.Organisation, r.Role, r.Note, r.Status,
            r.CreatedAt, r.ReviewedAt, r.ReviewedByEmail, null, null);
}
