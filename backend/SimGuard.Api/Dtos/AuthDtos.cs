using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

public record LoginRequest(string? Email, string? Password);

// Returned by POST /auth/login when the password is correct. Status is
// "mfaRequired" (send the token to /auth/mfa/verify) or
// "mfaEnrollmentRequired" (send it as a Bearer token to /auth/mfa/enroll/*).
public record LoginResponse(
    string Status,
    string MfaToken,
    DateTime ExpiresAt);

public record MfaVerifyRequest(string? MfaToken, string? Code);

public record MfaEnrollStartResponse(
    string SharedKey,
    string AuthenticatorUri);

public record MfaEnrollConfirmRequest(string? Code);

public record AccessTokenResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAt,
    bool MustChangePassword);

public record MfaEnrollConfirmResponse(
    IReadOnlyList<string> RecoveryCodes,
    string AccessToken,
    string TokenType,
    DateTime ExpiresAt,
    bool MustChangePassword);

public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public record MeResponse(
    int Id,
    string Email,
    string Name,
    string Role,
    int? EmployeeId,
    bool MfaEnabled,
    bool MustChangePassword);

public record AuthEventResponse(
    long Id,
    int? UserId,
    string? EmailAttempted,
    string EventType,
    bool Success,
    string? IpAddress,
    string? UserAgent,
    DateTime CreatedAt)
{
    public static readonly Expression<Func<AuthEvent, AuthEventResponse>> Projection =
        e => new AuthEventResponse(
            e.Id,
            e.UserId,
            e.EmailAttempted,
            e.EventType,
            e.Success,
            e.IpAddress,
            e.UserAgent,
            e.CreatedAt);
}
