using SimGuard.Api.Data;
using SimGuard.Api.Models;

namespace SimGuard.Api.Common.Authorization;

public static class AuthEventTypes
{
    public const string LoginSucceeded = "login_succeeded";
    public const string LoginFailed = "login_failed";
    public const string Lockout = "lockout";
    public const string MfaFailed = "mfa_failed";
    public const string MfaEnrolled = "mfa_enrolled";
    public const string MfaReset = "mfa_reset";
    public const string RecoveryCodeUsed = "recovery_code_used";
    public const string PasswordChanged = "password_changed";
    public const string PasswordReset = "password_reset";
    public const string UserCreated = "user_created";
    public const string RoleChanged = "role_changed";
    public const string UserDisabled = "user_disabled";
    public const string UserEnabled = "user_enabled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        LoginSucceeded, LoginFailed, Lockout, MfaFailed, MfaEnrolled, MfaReset,
        RecoveryCodeUsed, PasswordChanged, PasswordReset, UserCreated, RoleChanged,
        UserDisabled, UserEnabled
    };
}

// Writes rows to auth_events. Never pass a password, token or MFA code here.
public sealed class AuthEventLogger(SimGuardDbContext db, IHttpContextAccessor httpContextAccessor)
{
    private const int EmailMaxLength = 256;
    private const int IpMaxLength = 64;
    private const int UserAgentMaxLength = 512;

    public async Task LogAsync(
        string eventType,
        bool success,
        int? userId,
        string? email,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;

        db.AuthEvents.Add(new AuthEvent
        {
            UserId = userId,
            EmailAttempted = Truncate(email?.Trim(), EmailMaxLength),
            EventType = eventType,
            Success = success,
            IpAddress = Truncate(httpContext?.Connection.RemoteIpAddress?.ToString(), IpMaxLength),
            UserAgent = Truncate(httpContext?.Request.Headers.UserAgent.ToString(), UserAgentMaxLength),
            CreatedAt = Timestamps.UtcNow()
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value)
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];
}
