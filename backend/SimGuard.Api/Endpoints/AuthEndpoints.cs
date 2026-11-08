using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

// Sign-in flow (see backend/docs/auth.md):
//   POST /auth/login             email + password  -> MFA challenge or enrolment token
//   POST /auth/mfa/enroll/start  enrolment token   -> shared key + otpauth URI
//   POST /auth/mfa/enroll/confirm enrolment token + code -> recovery codes + access token
//   POST /auth/mfa/verify        challenge token + code  -> access token
public static class AuthEndpoints
{
    public const string MfaRequired = "mfaRequired";
    public const string MfaEnrollmentRequired = "mfaEnrollmentRequired";

    private const int RecoveryCodeCount = 10;
    private const string AuthenticatorIssuer = "SimGuard";
    private const string InvalidCredentials = "Invalid email or password.";
    private const string InvalidMfa = "The MFA token or code is invalid or has expired.";
    private const string RecoveryCodesLoginProvider = "[AspNetUserStore]";
    private const string RecoveryCodesTokenName = "RecoveryCodes";

    // Hashed once and checked against unknown emails so that a missing account
    // takes about as long to reject as a wrong password.
    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString()));

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth")
            .WithTags("Auth");

        group.MapPost("/login", Login)
            .WithName("Login")
            .AllowAnonymous()
            .RequireRateLimiting(SecurityServiceExtensions.AuthRateLimitPolicy)
            .Produces<LoginResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/mfa/verify", VerifyMfa)
            .WithName("VerifyMfa")
            .AllowAnonymous()
            .RequireRateLimiting(SecurityServiceExtensions.AuthRateLimitPolicy)
            .Produces<AccessTokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/mfa/enroll/start", StartMfaEnrollment)
            .WithName("StartMfaEnrollment")
            .RequireAuthorization(AuthPolicies.MfaEnrollment)
            .RequireRateLimiting(SecurityServiceExtensions.AuthRateLimitPolicy)
            .Produces<MfaEnrollStartResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/mfa/enroll/confirm", ConfirmMfaEnrollment)
            .WithName("ConfirmMfaEnrollment")
            .RequireAuthorization(AuthPolicies.MfaEnrollment)
            .RequireRateLimiting(SecurityServiceExtensions.AuthRateLimitPolicy)
            .Produces<MfaEnrollConfirmResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/me", GetMe)
            .WithName("GetMe")
            .RequireAuthorization(AuthPolicies.SignedIn)
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/change-password", ChangePassword)
            .WithName("ChangePassword")
            .RequireAuthorization(AuthPolicies.SignedIn)
            .RequireRateLimiting(SecurityServiceExtensions.AuthRateLimitPolicy)
            .Produces<AccessTokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/events", GetAuthEvents)
            .WithName("GetAuthEvents")
            .RequireAuthorization(AuthPolicies.AuditRead)
            .Produces<PagedResult<AuthEventResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return routes;
    }

    internal static async Task<string?> GetRoleAsync(UserManager<AppUser> userManager, AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles.Select(AppRoles.Normalize).FirstOrDefault(r => r is not null);
    }

    internal static Task<IdentityResult> ClearRecoveryCodesAsync(UserManager<AppUser> userManager, AppUser user) =>
        userManager.RemoveAuthenticationTokenAsync(user, RecoveryCodesLoginProvider, RecoveryCodesTokenName);

    private static async Task<IResult> Login(
        LoginRequest request,
        UserManager<AppUser> userManager,
        TokenService tokens,
        AuthEventLogger events,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var email = errors.RequiredString("email", request.Email, 256);

        if (string.IsNullOrEmpty(request.Password))
        {
            errors.Add("password", "password is required.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            new PasswordHasher<AppUser>().VerifyHashedPassword(new AppUser(), DummyPasswordHash.Value, request.Password!);
            await events.LogAsync(AuthEventTypes.LoginFailed, false, null, email, cancellationToken);
            return ApiProblems.Unauthorized(InvalidCredentials);
        }

        // Disabled and locked accounts get the same answer as a wrong password.
        if (user.IsDisabled || await userManager.IsLockedOutAsync(user))
        {
            await events.LogAsync(AuthEventTypes.LoginFailed, false, user.Id, email, cancellationToken);
            return ApiProblems.Unauthorized(InvalidCredentials);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password!))
        {
            await RecordFailureAsync(userManager, events, user, AuthEventTypes.LoginFailed, email, cancellationToken);
            return ApiProblems.Unauthorized(InvalidCredentials);
        }

        // The failed-attempt counter is reset only after the MFA step, so a
        // known password does not give unlimited guesses at the TOTP code.
        if (user.TwoFactorEnabled)
        {
            var challenge = tokens.CreateMfaChallengeToken(user);
            return TypedResults.Ok(new LoginResponse(MfaRequired, challenge.Token, challenge.ExpiresAt));
        }

        var enrollment = tokens.CreateMfaEnrollmentToken(user);
        return TypedResults.Ok(new LoginResponse(MfaEnrollmentRequired, enrollment.Token, enrollment.ExpiresAt));
    }

    private static async Task<IResult> VerifyMfa(
        MfaVerifyRequest request,
        UserManager<AppUser> userManager,
        TokenService tokens,
        AuthEventLogger events,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var mfaToken = errors.RequiredString("mfaToken", request.MfaToken, 4096);
        var code = NormalizeCode(errors.RequiredString("code", request.Code, 64));

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var identity = await tokens.ValidateMfaChallengeTokenAsync(mfaToken);
        var userId = identity is null ? null : TokenService.GetUserId(identity);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId.Value.ToString());

        if (identity is null
            || user is null
            || user.IsDisabled
            || !user.TwoFactorEnabled
            || identity.FindFirst(TokenService.SecurityStampClaim)?.Value
                != TokenService.HashSecurityStamp(user.SecurityStamp))
        {
            await events.LogAsync(AuthEventTypes.MfaFailed, false, user?.Id, user?.Email, cancellationToken);
            return ApiProblems.Unauthorized(InvalidMfa);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await events.LogAsync(AuthEventTypes.MfaFailed, false, user.Id, user.Email, cancellationToken);
            return ApiProblems.Unauthorized(InvalidMfa);
        }

        var usedRecoveryCode = false;
        bool valid;

        if (IsTotpCode(code))
        {
            valid = await userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, code);
        }
        else
        {
            valid = (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded;
            usedRecoveryCode = valid;
        }

        if (!valid)
        {
            await RecordFailureAsync(userManager, events, user, AuthEventTypes.MfaFailed, user.Email, cancellationToken);
            return ApiProblems.Unauthorized(InvalidMfa);
        }

        var role = await GetRoleAsync(userManager, user);

        if (role is null)
        {
            await events.LogAsync(AuthEventTypes.LoginFailed, false, user.Id, user.Email, cancellationToken);
            return ApiProblems.Unauthorized("The account has no role. Ask an Admin to assign one.");
        }

        await userManager.ResetAccessFailedCountAsync(user);

        if (usedRecoveryCode)
        {
            await events.LogAsync(AuthEventTypes.RecoveryCodeUsed, true, user.Id, user.Email, cancellationToken);
        }

        await events.LogAsync(AuthEventTypes.LoginSucceeded, true, user.Id, user.Email, cancellationToken);

        var access = tokens.CreateAccessToken(user, role);
        return TypedResults.Ok(new AccessTokenResponse(access.Token, "Bearer", access.ExpiresAt, user.MustChangePassword));
    }

    // Each call creates a new authenticator key, so an abandoned enrolment
    // can simply be started again.
    private static async Task<IResult> StartMfaEnrollment(
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager)
    {
        var user = await FindCurrentUserAsync(principal, userManager);

        if (user is null)
        {
            return ApiProblems.Unauthorized(InvalidMfa);
        }

        await userManager.ResetAuthenticatorKeyAsync(user);
        var key = await userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException("Identity did not return an authenticator key.");
        }

        var email = user.Email ?? string.Empty;
        var label = Uri.EscapeDataString($"{AuthenticatorIssuer}:{email}");
        var issuer = Uri.EscapeDataString(AuthenticatorIssuer);
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={issuer}&digits=6&period=30";

        return TypedResults.Ok(new MfaEnrollStartResponse(FormatKey(key), uri));
    }

    private static async Task<IResult> ConfirmMfaEnrollment(
        MfaEnrollConfirmRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        TokenService tokens,
        AuthEventLogger events,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var code = NormalizeCode(errors.RequiredString("code", request.Code, 64));

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var user = await FindCurrentUserAsync(principal, userManager);

        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return ApiProblems.Unauthorized(InvalidMfa);
        }

        if (string.IsNullOrEmpty(await userManager.GetAuthenticatorKeyAsync(user)))
        {
            return ApiProblems.Conflict("MFA enrolment has not been started. Call POST /api/auth/mfa/enroll/start first.");
        }

        var role = await GetRoleAsync(userManager, user);

        if (role is null)
        {
            return ApiProblems.Unauthorized("The account has no role. Ask an Admin to assign one.");
        }

        if (!IsTotpCode(code)
            || !await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            await RecordFailureAsync(userManager, events, user, AuthEventTypes.MfaFailed, user.Email, cancellationToken);
            errors.Add("code", "code is not valid. Enter the current 6-digit code from the authenticator app.");
            return errors.ToProblem();
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToList() ?? [];
        await userManager.ResetAccessFailedCountAsync(user);

        await events.LogAsync(AuthEventTypes.MfaEnrolled, true, user.Id, user.Email, cancellationToken);
        await events.LogAsync(AuthEventTypes.LoginSucceeded, true, user.Id, user.Email, cancellationToken);

        // SetTwoFactorEnabledAsync changed the security stamp; the token is
        // created from the updated user.
        var access = tokens.CreateAccessToken(user, role);

        return TypedResults.Ok(new MfaEnrollConfirmResponse(
            recoveryCodes,
            access.Token,
            "Bearer",
            access.ExpiresAt,
            user.MustChangePassword));
    }

    private static async Task<IResult> GetMe(
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager)
    {
        var user = await FindCurrentUserAsync(principal, userManager);

        if (user is null)
        {
            return ApiProblems.Unauthorized("The account no longer exists.");
        }

        var role = await GetRoleAsync(userManager, user) ?? string.Empty;

        return TypedResults.Ok(new MeResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.Name,
            role,
            user.EmployeeId,
            user.TwoFactorEnabled,
            user.MustChangePassword));
    }

    // Returns a new access token: changing the password changes the security
    // stamp, which invalidates the token used for this request.
    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        TokenService tokens,
        AuthEventLogger events,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrEmpty(request.CurrentPassword))
        {
            errors.Add("currentPassword", "currentPassword is required.");
        }

        if (string.IsNullOrEmpty(request.NewPassword))
        {
            errors.Add("newPassword", "newPassword is required.");
        }
        else if (request.NewPassword == request.CurrentPassword)
        {
            errors.Add("newPassword", "newPassword must be different from currentPassword.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var user = await FindCurrentUserAsync(principal, userManager);

        if (user is null)
        {
            return ApiProblems.Unauthorized("The account no longer exists.");
        }

        var role = await GetRoleAsync(userManager, user);

        if (role is null)
        {
            return ApiProblems.Unauthorized("The account has no role. Ask an Admin to assign one.");
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword!, request.NewPassword!);

        if (!result.Succeeded)
        {
            var mismatch = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch));

            if (mismatch)
            {
                await RecordFailureAsync(userManager, events, user, AuthEventTypes.PasswordChanged, user.Email, cancellationToken);
                errors.Add("currentPassword", "currentPassword is incorrect.");
            }

            foreach (var error in result.Errors.Where(e => e.Code != nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                errors.Add("newPassword", error.Description);
            }

            return errors.ToProblem();
        }

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        await events.LogAsync(AuthEventTypes.PasswordChanged, true, user.Id, user.Email, cancellationToken);

        var access = tokens.CreateAccessToken(user, role);
        return TypedResults.Ok(new AccessTokenResponse(access.Token, "Bearer", access.ExpiresAt, false));
    }

    private static async Task<IResult> GetAuthEvents(
        string? type,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? userId,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);
        var normalizedType = type?.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(normalizedType) && !AuthEventTypes.All.Contains(normalizedType))
        {
            errors.Add("type", $"type must be one of: {string.Join(", ", AuthEventTypes.All)}.");
        }

        if (from is not null && to is not null && from > to)
        {
            errors.Add("from", "from must be earlier than or equal to to.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var query = db.AuthEvents.AsNoTracking();

        if (!string.IsNullOrEmpty(normalizedType))
        {
            query = query.Where(e => e.EventType == normalizedType);
        }

        if (from is not null)
        {
            var fromUtc = from.Value.UtcDateTime;
            query = query.Where(e => e.CreatedAt >= fromUtc);
        }

        if (to is not null)
        {
            var toUtc = to.Value.UtcDateTime;
            query = query.Where(e => e.CreatedAt <= toUtc);
        }

        if (userId is not null)
        {
            query = query.Where(e => e.UserId == userId);
        }

        var result = await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Select(AuthEventResponse.Projection)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task RecordFailureAsync(
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        AppUser user,
        string eventType,
        string? email,
        CancellationToken cancellationToken)
    {
        await userManager.AccessFailedAsync(user);
        await events.LogAsync(eventType, false, user.Id, email, cancellationToken);

        if (await userManager.IsLockedOutAsync(user))
        {
            await events.LogAsync(AuthEventTypes.Lockout, true, user.Id, email, cancellationToken);
        }
    }

    private static async Task<AppUser?> FindCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager)
    {
        var userId = TokenService.GetUserId(principal);
        return userId is null ? null : await userManager.FindByIdAsync(userId.Value.ToString());
    }

    private static string NormalizeCode(string code) =>
        code.Replace(" ", string.Empty);

    private static bool IsTotpCode(string code) =>
        code.Length == 6 && code.All(char.IsAsciiDigit);

    // Groups of four lower-case characters, easier to type by hand.
    private static string FormatKey(string key)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return builder.ToString().ToLowerInvariant();
    }
}
