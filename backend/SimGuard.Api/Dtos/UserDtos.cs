namespace SimGuard.Api.Dtos;

public record CreateUserRequest(
    string? Email,
    string? Name,
    string? Role,
    int? EmployeeId,
    string? TemporaryPassword);

public record ChangeRoleRequest(string? Role);

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
    DateTime CreatedAt);
