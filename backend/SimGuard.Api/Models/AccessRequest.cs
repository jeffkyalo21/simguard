namespace SimGuard.Api.Models;

// A visitor's request for an account, sent from the landing page. An Admin
// reviews it; approving creates the account and emails its sign-in details.
// Contract in backend/docs/access-requests.md.
public class AccessRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Organisation { get; set; } = string.Empty;

    // SecurityOfficer or Auditor.
    public string Role { get; set; } = string.Empty;

    public string? Note { get; set; }

    // pending, approved or declined.
    public string Status { get; set; } = AccessRequestStatuses.Pending;

    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByEmail { get; set; }
}

public static class AccessRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Declined = "declined";

    public static readonly IReadOnlyList<string> All = [Pending, Approved, Declined];
}
