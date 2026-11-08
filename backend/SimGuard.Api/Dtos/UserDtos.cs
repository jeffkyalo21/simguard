namespace SimGuard.Api.Dtos;

// TemporaryPassword is only accepted in Development (test scripts). Elsewhere
// the API generates the password and emails it to the person.
public record CreateUserRequest(
    string? Email,
    string? Name,
    string? Role,
    int? EmployeeId,
    string? TemporaryPassword = null);

public record ChangeRoleRequest(string? Role);

// Development only. Elsewhere use POST /api/users/{id}/signin-email.
public record ResetPasswordRequest(string? TemporaryPassword);

public record UserResponse(
    int Id,
    string Email,
    string Name,
    string Role,
    int? EmployeeId,
    bool MfaEnabled,
    bool MustChangePassword,
    bool IsDisabled,
    DateTime? LockedOutUntil,
    DateTime CreatedAt,
    // Only set by the calls that email sign-in details (invite, resend):
    // true when the provider accepted the email, false when the Admin must
    // try again. Null everywhere else.
    bool? EmailSent = null);
