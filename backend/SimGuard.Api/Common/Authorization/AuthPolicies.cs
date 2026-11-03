using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace SimGuard.Api.Common.Authorization;

// All authorization policies are defined here. The role matrix is documented
// in backend/docs/auth.md; keep the two in sync.
public static class AuthPolicies
{
    // Any signed-in user (Admin, SecurityOfficer, Auditor).
    public const string CanRead = nameof(CanRead);

    // Departments and employees: create, update, delete.
    public const string CanManageOrganisation = nameof(CanManageOrganisation);

    // SIMs: create, update, delete.
    public const string CanManageSims = nameof(CanManageSims);

    // SIMs: PATCH /sims/{id}/status.
    public const string CanChangeSimStatus = nameof(CanChangeSimStatus);

    // Allocations: create and return.
    public const string CanManageAllocations = nameof(CanManageAllocations);

    // /api/users.
    public const string CanManageUsers = nameof(CanManageUsers);

    // GET /api/auth/events.
    public const string CanReadAudit = nameof(CanReadAudit);

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
            // token and a completed password change.
            .SetFallbackPolicy(FullAccess().Build())
            .SetDefaultPolicy(FullAccess().Build())
            .AddPolicy(CanRead, FullAccess()
                .RequireRole(AppRoles.Admin, AppRoles.SecurityOfficer, AppRoles.Auditor)
                .Build())
            .AddPolicy(CanManageOrganisation, FullAccess()
                .RequireRole(AppRoles.Admin)
                .Build())
            .AddPolicy(CanManageSims, FullAccess()
                .RequireRole(AppRoles.Admin)
                .Build())
            .AddPolicy(CanChangeSimStatus, FullAccess()
                .RequireRole(AppRoles.Admin, AppRoles.SecurityOfficer)
                .Build())
            .AddPolicy(CanManageAllocations, FullAccess()
                .RequireRole(AppRoles.Admin, AppRoles.SecurityOfficer)
                .Build())
            .AddPolicy(CanManageUsers, FullAccess()
                .RequireRole(AppRoles.Admin)
                .Build())
            .AddPolicy(CanReadAudit, FullAccess()
                .RequireRole(AppRoles.Admin, AppRoles.Auditor)
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
