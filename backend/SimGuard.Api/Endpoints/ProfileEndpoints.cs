using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

public record UpdateProfileRequest(string? Name);

// Password and a current authenticator code, for actions that need both.
public record ConfirmIdentityRequest(string? Password, string? Code);

public record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

// The signed-in user's own profile: name, new recovery codes and deleting the
// account. Contract in backend/docs/auth.md. Every role may use them.
public static class ProfileEndpoints
{
    private const int NameMaxLength = 100;
    private const string WrongPassword = "The password is incorrect.";
    private const string WrongCode = "The code is not valid. Enter the current 6-digit code from your authenticator app.";

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth").WithTags("Auth");

        group.MapPut("/me", UpdateProfile)
            .WithName("UpdateProfile")
            .RequireAuthorization(AuthPolicies.AnyRole)
            .Produces<MeResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/mfa/recovery-codes", RegenerateRecoveryCodes)
            .WithName("RegenerateRecoveryCodes")
            .RequireAuthorization(AuthPolicies.AnyRole)
            .RequireRateLimiting(SecurityServiceExtensions.AuthRateLimitPolicy)
            .Produces<RecoveryCodesResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/me/delete", DeleteAccount)
            .WithName("DeleteMyAccount")
            .RequireAuthorization(AuthPolicies.AnyRole)
            .RequireRateLimiting(SecurityServiceExtensions.AuthRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return routes;
    }

    private static async Task<IResult> UpdateProfile(
        UpdateProfileRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = errors.RequiredString("name", request.Name, NameMaxLength);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var user = await AuthEndpoints.FindCurrentUserAsync(principal, userManager);

        if (user is null)
        {
            return ApiProblems.Unauthorized("The account no longer exists.");
        }

        var before = user.Name;

        if (before != name)
        {
            user.Name = name;
            UserEndpoints.EnsureSucceeded(await userManager.UpdateAsync(user));
            audit.Add(AuditActions.UserProfileUpdated, null, new { id = user.Id, name = before }, new { id = user.Id, name });
            await db.SaveChangesAsync(cancellationToken);
        }

        var role = await AuthEndpoints.GetRoleAsync(userManager, user) ?? string.Empty;
        return TypedResults.Ok(new MeResponse(
            user.Id, user.Email ?? string.Empty, user.Name, role, user.EmployeeId, user.TwoFactorEnabled, user.MustChangePassword));
    }

    // Checks the password and a current authenticator code. A wrong one counts
    // as a failed attempt (lockout applies). Answers 400 with field errors, not
    // 401, because 401 means the session ended.
    private static async Task<IResult?> CheckIdentityAsync(
        ConfirmIdentityRequest request,
        AppUser user,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrEmpty(request.Password))
        {
            errors.Add("password", "password is required.");
        }

        var code = AuthEndpoints.NormalizeCode(request.Code ?? string.Empty);

        if (code.Length == 0)
        {
            errors.Add("code", "code is required.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            errors.Add("password", "Too many failed attempts. Try again later.");
            return errors.ToProblem();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password!))
        {
            await AuthEndpoints.RecordFailureAsync(userManager, events, user, AuthEventTypes.LoginFailed, user.Email, cancellationToken);
            errors.Add("password", WrongPassword);
            return errors.ToProblem();
        }

        if (!AuthEndpoints.IsTotpCode(code)
            || !await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            await AuthEndpoints.RecordFailureAsync(userManager, events, user, AuthEventTypes.MfaFailed, user.Email, cancellationToken);
            errors.Add("code", WrongCode);
            return errors.ToProblem();
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return null;
    }

    private static async Task<IResult> RegenerateRecoveryCodes(
        ConfirmIdentityRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var user = await AuthEndpoints.FindCurrentUserAsync(principal, userManager);

        if (user is null)
        {
            return ApiProblems.Unauthorized("The account no longer exists.");
        }

        var refused = await CheckIdentityAsync(request, user, userManager, events, cancellationToken);

        if (refused is not null)
        {
            return refused;
        }

        // Replaces every earlier code. The codes are returned once and not stored in clear text.
        var codes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, AuthEndpoints.RecoveryCodeCount))?.ToList() ?? [];

        await events.LogAsync(AuthEventTypes.RecoveryCodesRegenerated, true, user.Id, user.Email, cancellationToken);
        await audit.WriteAsync(AuditActions.UserRecoveryCodesRegenerated, null, null, new { id = user.Id }, cancellationToken);

        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    private static async Task<IResult> DeleteAccount(
        ConfirmIdentityRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var user = await AuthEndpoints.FindCurrentUserAsync(principal, userManager);

        if (user is null)
        {
            return ApiProblems.Unauthorized("The account no longer exists.");
        }

        var refused = await CheckIdentityAsync(request, user, userManager, events, cancellationToken);

        if (refused is not null)
        {
            return refused;
        }

        var id = user.Id;
        var oldEmail = user.Email ?? string.Empty;
        var role = await AuthEndpoints.GetRoleAsync(userManager, user);
        var placeholderName = $"Deleted user {id}";
        var placeholderEmail = $"deleted-user-{id}@deleted.invalid";

        await using (var transaction = await UserEndpoints.BeginAdminChangeAsync(db, cancellationToken))
        {
            if (role == AppRoles.Admin && await UserEndpoints.CountActiveAdminsAsync(db, cancellationToken) <= 1)
            {
                return ApiProblems.Conflict(
                    "You are the last active Admin, so your account cannot be deleted. Make another user an Admin first.");
            }

            // Personal data goes; the row stays so audit rows keep a stable user id and role.
            user.Name = placeholderName;
            user.PhoneNumber = null;
            user.EmployeeId = null;
            user.IsDisabled = true;
            user.MustChangePassword = false;
            UserEndpoints.EnsureSucceeded(await userManager.SetUserNameAsync(user, placeholderEmail));
            UserEndpoints.EnsureSucceeded(await userManager.SetEmailAsync(user, placeholderEmail));
            UserEndpoints.EnsureSucceeded(await userManager.SetTwoFactorEnabledAsync(user, false));
            UserEndpoints.EnsureSucceeded(await userManager.RemovePasswordAsync(user));
            await userManager.RemoveAuthenticationTokenAsync(user, AuthEndpoints.RecoveryCodesLoginProvider, "AuthenticatorKey");
            await AuthEndpoints.ClearRecoveryCodesAsync(userManager, user);

            // Ends every session: the stamp in existing tokens no longer matches.
            UserEndpoints.EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));

            // No password, code or email in the entry: the id and role identify the account.
            await audit.WriteAsync(
                AuditActions.UserSelfDeleted,
                null,
                new { id, role },
                new { id, role, isDisabled = true },
                cancellationToken);

            // Replace the old email in the user's audit rows and auth events.
            await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE audit_logs SET actor_email = {placeholderName} WHERE actor_user_id = {id}
                """, cancellationToken);
            await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE audit_logs
                SET old_values = replace(old_values::text, {oldEmail}, {placeholderEmail})::jsonb,
                    new_values = replace(new_values::text, {oldEmail}, {placeholderEmail})::jsonb
                WHERE old_values::text LIKE '%' || {oldEmail} || '%' OR new_values::text LIKE '%' || {oldEmail} || '%'
                """, cancellationToken);
            await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE auth_events SET email_attempted = {placeholderEmail}, ip_address = NULL, user_agent = NULL
                WHERE user_id = {id} OR lower(email_attempted) = lower({oldEmail})
                """, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        await events.LogAsync(AuthEventTypes.AccountDeleted, true, id, placeholderEmail, cancellationToken);
        return TypedResults.NoContent();
    }
}
