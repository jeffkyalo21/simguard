using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace SimGuard.Api.Common.Authorization;

// All authorization policies are defined here. The role matrix is documented
// in backend/docs/auth.md; keep the two in sync.
//
// The roles split the system in two:
//   Admin           operates SimGuard (accounts, system audit). Sees no
//                   organisation data.
//   SecurityOfficer manages the organisation's data.
//   Auditor         reads the organisation's data and the full audit log;
//                   never writes.
public static class AuthPolicies
{
    // Organisation data, read: departments, employees, SIMs, allocations,
    // alerts, carriers, risk scores, usage, dashboard. SecurityOfficer and
    // Auditor.
    public const string OrgRead = nameof(OrgRead);

    // Organisation data, every write (create, update, delete, SIM status and
    // tier, allocations and returns, alert review). SecurityOfficer only.
    public const string OrgWrite = nameof(OrgWrite);

    // SimGuard operation: /api/users. Admin only.
    public const string AdminOnly = nameof(AdminOnly);

    // GET /api/audit-logs and GET /api/auth/events. Admin and Auditor. The
    // audit log endpoint narrows Admin to system entries.
    public const string AuditRead = nameof(AuditRead);

    // POST /api/dev/seed-demo, mapped only in Development. Admin. The one
    // exception to "Admin sees no organisation data": it is a dev tool.
    public const string DevTools = nameof(DevTools);

    // Any role with a full access token. GET /health/database.
    public const string AnyRole = nameof(AnyRole);

    // GET /api/auth/me and POST /api/auth/change-password. The only policy a
    // user with a pending forced password change can satisfy.
    public const string SignedIn = nameof(SignedIn);

    // The MFA enrolment endpoints. Satisfied only by the short-lived
    // enrolment token returned by POST /api/auth/login.
    public const string MfaEnrollment = nameof(MfaEnrollment);

    // Added to the identity by JwtBearer OnTokenValidated when the account
    // still has to change its password.
    public const string MustChangePasswordClaim = "must_change_password";

    public static IServiceCollection AddSimGuardAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // Every endpoint without its own policy requires a full access
            // token and a completed password change. Every mapped endpoint
            // still names its policy explicitly.
            .SetFallbackPolicy(FullAccess().Build())
            .SetDefaultPolicy(FullAccess().Build())
            .AddPolicy(OrgRead, FullAccess()
                .RequireRole(AppRoles.SecurityOfficer, AppRoles.Auditor)
                .Build())
            .AddPolicy(OrgWrite, FullAccess()
                .RequireRole(AppRoles.SecurityOfficer)
                .Build())
            .AddPolicy(AdminOnly, FullAccess()
                .RequireRole(AppRoles.Admin)
                .Build())
            .AddPolicy(AuditRead, FullAccess()
                .RequireRole(AppRoles.Admin, AppRoles.Auditor)
                .Build())
            .AddPolicy(DevTools, FullAccess()
                .RequireRole(AppRoles.Admin)
                .Build())
            .AddPolicy(AnyRole, FullAccess()
                .RequireRole(AppRoles.Admin, AppRoles.SecurityOfficer, AppRoles.Auditor)
                .Build())
            .AddPolicy(SignedIn, new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(MfaEnrollment, new AuthorizationPolicyBuilder(TokenService.MfaEnrollmentScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(TokenService.PurposeClaim, TokenService.MfaEnrollmentPurpose)
                .Build());

        return services;
    }

    private static AuthorizationPolicyBuilder FullAccess() =>
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                !context.User.HasClaim(MustChangePasswordClaim, "true"));
}
