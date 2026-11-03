namespace SimGuard.Api.Common.Authorization;

// Every account has exactly one of these roles.
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string SecurityOfficer = "SecurityOfficer";
    public const string Auditor = "Auditor";

    public static readonly IReadOnlyList<string> All = [Admin, SecurityOfficer, Auditor];

    // Accepts any casing and returns the canonical name, or null if unknown.
    public static string? Normalize(string? role)
    {
        var trimmed = role?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? null
            : All.FirstOrDefault(r => string.Equals(r, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
