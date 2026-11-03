using Microsoft.AspNetCore.Identity;

namespace SimGuard.Api.Models;

// A login account. Kept separate from Employee: not every employee signs in,
// and an account can exist without an employee record (for example an
// external auditor). EmployeeId links the two when both exist.
public class AppUser : IdentityUser<int>
{
    public string Name { get; set; } = string.Empty;
    public int? EmployeeId { get; set; }

    // Set when an Admin creates the account or resets its password. While it
    // is true, the user can only call GET /api/auth/me and
    // POST /api/auth/change-password.
    public bool MustChangePassword { get; set; }

    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }

    public Employee? Employee { get; set; }
}
