namespace SimGuard.Api.Models;

// Security log for sign-in and account administration. Separate from
// AuditLog, whose UserId points to an Employee.
public class AuthEvent
{
    public long Id { get; set; }

    // The account the event is about. Null when the email did not match any account.
    public int? UserId { get; set; }

    public string? EmailAttempted { get; set; }
    public string EventType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }

    public AppUser? User { get; set; }
}
