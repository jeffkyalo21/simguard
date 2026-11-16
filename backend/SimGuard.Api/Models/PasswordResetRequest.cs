namespace SimGuard.Api.Models;

// One emailed password reset link. Only the SHA-256 hash of the token is
// stored; the token itself exists only in the email. A request is usable
// while UsedAt is null and ExpiresAt is in the future. UsedAt is also set when
// a newer request for the same account replaces it.
public class PasswordResetRequest
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public AppUser? User { get; set; }
}
