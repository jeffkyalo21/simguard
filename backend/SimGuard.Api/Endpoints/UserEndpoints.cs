using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Email;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

// Account administration. Admin only. There is no self-registration and no
// delete: accounts are disabled instead, so auth events keep their user.
public static class UserEndpoints
{
    private const int EmailMaxLength = 256;
    private const int NameMaxLength = 150;

    // Key for pg_advisory_xact_lock. Serialises changes that could remove the
    // last active Admin, so two Admins cannot demote each other at once.
    private const long AdminChangeLockKey = 0x5347_4144_4D49_4E;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/users")
            .WithTags("Users")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapGet("/", GetUsers)
            .WithName("GetUsers")
            .Produces<PagedResult<UserResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:int}", GetUser)
            .WithName("GetUser")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateUser)
            .WithName("CreateUser")
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:int}/role", ChangeRole)
            .WithName("ChangeUserRole")
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:int}/disable", DisableUser)
            .WithName("DisableUser")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:int}/enable", EnableUser)
            .WithName("EnableUser")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:int}/mfa/reset", ResetMfa)
            .WithName("ResetUserMfa")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:int}/unlock", UnlockUser)
            .WithName("UnlockUser")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Sets a new generated password and emails it. The Admin never sees it.
        group.MapPost("/{id:int}/signin-email", ResendSignInEmail)
            .WithName("ResendUserSignInEmail")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Development only: sets a password the caller chose, for test scripts.
        group.MapPost("/{id:int}/password/reset", ResetPassword)
            .WithName("ResetUserPassword")
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GetUsers(
        string? role,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);
        string? normalizedRole = null;

        if (!string.IsNullOrWhiteSpace(role))
        {
            normalizedRole = AppRoles.Normalize(role);

            if (normalizedRole is null)
            {
                errors.Add("role", $"role must be one of: {string.Join(", ", AppRoles.All)}.");
            }
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var query = QueryUserRows(db);

        if (normalizedRole is not null)
        {
            query = query.Where(u => u.Role == normalizedRole);
        }

        var rows = await query
            .OrderBy(u => u.Id)
            .ToPagedResultAsync(paging, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        return TypedResults.Ok(new PagedResult<UserResponse>(
            rows.Items.Select(r => r.ToResponse(now)).ToList(),
            rows.Page,
            rows.PageSize,
            rows.Total));
    }

    private static async Task<IResult> GetUser(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await FindUserResponseAsync(db, id, cancellationToken);

        return user is null
            ? ApiProblems.NotFound($"User {id} was not found.")
            : TypedResults.Ok(user);
    }

    // Invites a person: the API generates a password and emails it. Only in
    // Development can the caller choose the password (test scripts).
    private static async Task<IResult> CreateUser(
        CreateUserRequest request,
        SimGuardDbContext db,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        AuditWriter audit,
        IWebHostEnvironment environment,
        IEmailSender emailSender,
        IOptions<BrevoOptions> emailOptions,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var email = errors.RequiredEmail("email", request.Email, EmailMaxLength);
        var name = errors.RequiredString("name", request.Name, NameMaxLength);
        var role = ValidateRole(request.Role, errors);

        if (request.EmployeeId is not null && request.EmployeeId <= 0)
        {
            errors.Add("employeeId", "employeeId must be a positive integer.");
        }

        RejectManualPassword(request.TemporaryPassword, environment, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var issuedByApi = string.IsNullOrEmpty(request.TemporaryPassword);
        var password = issuedByApi ? SignInCredentials.GeneratePassword() : request.TemporaryPassword!;

        if (request.EmployeeId is { } employeeId)
        {
            if (!await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
            {
                errors.Add("employeeId", $"Employee {employeeId} does not exist.");
                return errors.ToProblem();
            }

            if (await db.Users.AnyAsync(u => u.EmployeeId == employeeId, cancellationToken))
            {
                return ApiProblems.Conflict($"Employee {employeeId} already has a user account.");
            }
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return ApiProblems.Conflict($"A user with email '{email}' already exists.");
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Name = name,
            EmployeeId = request.EmployeeId,
            MustChangePassword = true,
            CreatedAt = Timestamps.UtcNow()
        };

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var created = await userManager.CreateAsync(user, password);

            if (!created.Succeeded)
            {
                return IdentityProblem(created, "temporaryPassword");
            }

            var added = await userManager.AddToRoleAsync(user, role!);

            if (!added.Succeeded)
            {
                throw new InvalidOperationException($"Could not add user to role {role}.");
            }

            await audit.WriteAsync(
                AuditActions.UserCreated,
                null,
                null,
                new { id = user.Id, email = user.Email, name = user.Name, role, employeeId = user.EmployeeId },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        await events.LogAsync(AuthEventTypes.UserCreated, true, user.Id, email, cancellationToken);

        // The account is saved first; a failed email never undoes it. The
        // Admin can use "resend sign-in email" when emailSent is false.
        bool? emailSent = null;

        if (issuedByApi)
        {
            emailSent = await SignInCredentials.SendAsync(
                emailSender, emailOptions.Value, name, email, password, cancellationToken);
        }

        return TypedResults.Created(
            $"/api/users/{user.Id}",
            (await FindUserResponseAsync(db, user.Id, cancellationToken))! with { EmailSent = emailSent });
    }

    private static async Task<IResult> ChangeRole(
        int id,
        ChangeRoleRequest request,
        ClaimsPrincipal principal,
        SimGuardDbContext db,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var role = ValidateRole(request.Role, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var user = await userManager.FindByIdAsync(id.ToString());

        if (user is null)
        {
            return ApiProblems.NotFound($"User {id} was not found.");
        }

        var currentRole = await AuthEndpoints.GetRoleAsync(userManager, user);

        if (currentRole == role)
        {
            return TypedResults.Ok(await FindUserResponseAsync(db, id, cancellationToken));
        }

        if (id == TokenService.GetUserId(principal))
        {
            return ApiProblems.Conflict("You cannot change your own role. Ask another Admin.");
        }

        await using (var transaction = await BeginAdminChangeAsync(db, cancellationToken))
        {
            if (currentRole == AppRoles.Admin
                && !user.IsDisabled
                && await CountActiveAdminsAsync(db, cancellationToken) <= 1)
            {
                return ApiProblems.Conflict("This is the last active Admin. Promote another user to Admin first.");
            }

            var existingRoles = await userManager.GetRolesAsync(user);

            if (existingRoles.Count > 0)
            {
                EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, existingRoles));
            }

            EnsureSucceeded(await userManager.AddToRoleAsync(user, role!));

            // Invalidates tokens that still carry the old role.
            EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));

            await audit.WriteAsync(
                AuditActions.UserRoleChanged,
                null,
                new { id = user.Id, email = user.Email, role = currentRole },
                new { id = user.Id, email = user.Email, role },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        await events.LogAsync(AuthEventTypes.RoleChanged, true, user.Id, user.Email, cancellationToken);

        return TypedResults.Ok(await FindUserResponseAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> DisableUser(
        int id,
        ClaimsPrincipal principal,
        SimGuardDbContext db,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());

        if (user is null)
        {
            return ApiProblems.NotFound($"User {id} was not found.");
        }

        if (id == TokenService.GetUserId(principal))
        {
            return ApiProblems.Conflict("You cannot disable your own account. Ask another Admin.");
        }

        if (user.IsDisabled)
        {
            return TypedResults.Ok(await FindUserResponseAsync(db, id, cancellationToken));
        }

        await using (var transaction = await BeginAdminChangeAsync(db, cancellationToken))
        {
            if (await AuthEndpoints.GetRoleAsync(userManager, user) == AppRoles.Admin
                && await CountActiveAdminsAsync(db, cancellationToken) <= 1)
            {
                return ApiProblems.Conflict("This is the last active Admin and cannot be disabled.");
            }

            user.IsDisabled = true;
            EnsureSucceeded(await userManager.UpdateAsync(user));

            // Invalidates the user's current tokens immediately.
            EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));

            await audit.WriteAsync(
                AuditActions.UserDisabled,
                null,
                new { id = user.Id, email = user.Email, isDisabled = false },
                new { id = user.Id, email = user.Email, isDisabled = true },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        await events.LogAsync(AuthEventTypes.UserDisabled, true, user.Id, user.Email, cancellationToken);

        return TypedResults.Ok(await FindUserResponseAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> EnableUser(
        int id,
        SimGuardDbContext db,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());

        if (user is null)
        {
            return ApiProblems.NotFound($"User {id} was not found.");
        }

        if (user.IsDisabled)
        {
            user.IsDisabled = false;

            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                EnsureSucceeded(await userManager.UpdateAsync(user));
                await audit.WriteAsync(
                    AuditActions.UserEnabled,
                    null,
                    new { id = user.Id, email = user.Email, isDisabled = true },
                    new { id = user.Id, email = user.Email, isDisabled = false },
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            await events.LogAsync(AuthEventTypes.UserEnabled, true, user.Id, user.Email, cancellationToken);
        }

        return TypedResults.Ok(await FindUserResponseAsync(db, id, cancellationToken));
    }

    // The user enrols again at the next sign-in. Their current tokens stop
    // working because the security stamp changes.
    private static async Task<IResult> ResetMfa(
        int id,
        SimGuardDbContext db,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());

        if (user is null)
        {
            return ApiProblems.NotFound($"User {id} was not found.");
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            EnsureSucceeded(await userManager.SetTwoFactorEnabledAsync(user, false));
            EnsureSucceeded(await userManager.ResetAuthenticatorKeyAsync(user));
            EnsureSucceeded(await AuthEndpoints.ClearRecoveryCodesAsync(userManager, user));
            await audit.WriteAsync(
                AuditActions.UserMfaReset,
                null,
                null,
                new { id = user.Id, email = user.Email, mfaEnabled = false },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await events.LogAsync(AuthEventTypes.MfaReset, true, user.Id, user.Email, cancellationToken);

        return TypedResults.Ok(await FindUserResponseAsync(db, id, cancellationToken));
    }

    // Clears any lockout, so the person can try to sign in again.
    private static async Task<IResult> UnlockUser(
        int id,
        SimGuardDbContext db,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());

        if (user is null)
        {
            return ApiProblems.NotFound($"User {id} was not found.");
        }

        if (user.LockoutEnd > DateTimeOffset.UtcNow || user.AccessFailedCount > 0)
        {
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, null));
                EnsureSucceeded(await userManager.ResetAccessFailedCountAsync(user));
                await audit.WriteAsync(
                    AuditActions.UserUnlocked,
                    null,
                    null,
                    new { id = user.Id, email = user.Email },
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            await events.LogAsync(AuthEventTypes.UserUnlocked, true, user.Id, user.Email, cancellationToken);
        }

        return TypedResults.Ok(await FindUserResponseAsync(db, id, cancellationToken));
    }

    // Replaces the password with a new generated one and emails it. Works for
    // an account that never signed in and for one whose owner is locked out.
    // The Admin never sees the password. The person must change it at the next
    // sign-in, and any lockout is cleared.
    private static async Task<IResult> ResendSignInEmail(
        int id,
        ClaimsPrincipal principal,
        SimGuardDbContext db,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        AuditWriter audit,
        IEmailSender emailSender,
        IOptions<BrevoOptions> emailOptions,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());

        if (user is null)
        {
            return ApiProblems.NotFound($"User {id} was not found.");
        }

        // Replacing your own password would leave you depending on an email
        // that may fail. Another Admin does it for you.
        if (id == TokenService.GetUserId(principal))
        {
            return ApiProblems.Conflict("You cannot send sign-in details to your own account. Ask another Admin.");
        }

        if (user.IsDisabled)
        {
            return ApiProblems.Conflict("This account is disabled. Enable it before sending sign-in details.");
        }

        var password = SignInCredentials.GeneratePassword();

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            EnsureSucceeded(await userManager.ResetPasswordAsync(user, resetToken, password));

            user.MustChangePassword = true;
            EnsureSucceeded(await userManager.UpdateAsync(user));
            EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, null));
            EnsureSucceeded(await userManager.ResetAccessFailedCountAsync(user));

            // The password itself is never recorded.
            await audit.WriteAsync(
                AuditActions.UserSignInEmailSent,
                null,
                null,
                new { id = user.Id, email = user.Email, mustChangePassword = true },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        await events.LogAsync(AuthEventTypes.PasswordReset, true, user.Id, user.Email, cancellationToken);

        var emailSent = await SignInCredentials.SendAsync(
            emailSender, emailOptions.Value, user.Name, user.Email!, password, cancellationToken);

        return TypedResults.Ok(
            (await FindUserResponseAsync(db, id, cancellationToken))! with { EmailSent = emailSent });
    }

    // Development only. Sets a temporary password the caller chose, clears any
    // lockout and forces a password change at the next sign-in.
    private static async Task<IResult> ResetPassword(
        int id,
        ResetPasswordRequest request,
        SimGuardDbContext db,
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        AuditWriter audit,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrEmpty(request.TemporaryPassword))
        {
            errors.Add("temporaryPassword", "temporaryPassword is required.");
        }

        RejectManualPassword(request.TemporaryPassword, environment, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var user = await userManager.FindByIdAsync(id.ToString());

        if (user is null)
        {
            return ApiProblems.NotFound($"User {id} was not found.");
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await userManager.ResetPasswordAsync(user, resetToken, request.TemporaryPassword!);

            if (!reset.Succeeded)
            {
                return IdentityProblem(reset, "temporaryPassword");
            }

            user.MustChangePassword = true;
            EnsureSucceeded(await userManager.UpdateAsync(user));
            EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, null));
            EnsureSucceeded(await userManager.ResetAccessFailedCountAsync(user));

            // The temporary password itself is never recorded.
            await audit.WriteAsync(
                AuditActions.UserPasswordReset,
                null,
                null,
                new { id = user.Id, email = user.Email, mustChangePassword = true },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        await events.LogAsync(AuthEventTypes.PasswordReset, true, user.Id, user.Email, cancellationToken);

        return TypedResults.Ok(await FindUserResponseAsync(db, id, cancellationToken));
    }

    internal static async Task<IDbContextTransaction> BeginAdminChangeAsync(
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdminChangeLockKey})", cancellationToken);
        return transaction;
    }

    internal static Task<int> CountActiveAdminsAsync(
        SimGuardDbContext db,
        CancellationToken cancellationToken) =>
        QueryUserRows(db)
            .CountAsync(u => u.Role == AppRoles.Admin && !u.IsDisabled, cancellationToken);

    private static string? ValidateRole(string? role, ValidationErrors errors)
    {
        var normalized = AppRoles.Normalize(role);

        if (normalized is null)
        {
            errors.Add("role", $"role must be one of: {string.Join(", ", AppRoles.All)}.");
        }

        return normalized;
    }

    // An Admin must not choose another person's password. Development keeps
    // it so the smoke scripts can create accounts they can sign in to.
    internal static void RejectManualPassword(string? password, IWebHostEnvironment environment, ValidationErrors errors)
    {
        if (!string.IsNullOrEmpty(password) && !environment.IsDevelopment())
        {
            errors.Add(
                "temporaryPassword",
                "A password cannot be set by hand. SimGuard generates it and emails it to the person.");
        }
    }

    internal static IResult IdentityProblem(IdentityResult result, string passwordField)
    {
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return ApiProblems.Conflict("A user with the same email already exists.");
        }

        var errors = new ValidationErrors();

        foreach (var error in result.Errors)
        {
            errors.Add(error.Code.StartsWith("Password") ? passwordField : "email", error.Description);
        }

        return errors.ToProblem();
    }

    internal static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity operation failed: " + string.Join(" ", result.Errors.Select(e => e.Code)));
        }
    }

    private static async Task<UserResponse?> FindUserResponseAsync(
        SimGuardDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        var row = await QueryUserRows(db).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return row?.ToResponse(DateTimeOffset.UtcNow);
    }

    private static IQueryable<UserRow> QueryUserRows(SimGuardDbContext db) =>
        db.Users
            .AsNoTracking()
            .Select(u => new UserRow
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                Name = u.Name,
                Role = db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .FirstOrDefault() ?? string.Empty,
                EmployeeId = u.EmployeeId,
                MfaEnabled = u.TwoFactorEnabled,
                MustChangePassword = u.MustChangePassword,
                IsDisabled = u.IsDisabled,
                LockoutEnd = u.LockoutEnd,
                CreatedAt = u.CreatedAt
            });

    // Object initializer rather than a constructor so EF Core can translate
    // filters on Role and IsDisabled that are applied after the projection.
    private sealed class UserRow
    {
        public int Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public int? EmployeeId { get; init; }
        public bool MfaEnabled { get; init; }
        public bool MustChangePassword { get; init; }
        public bool IsDisabled { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
        public DateTime CreatedAt { get; init; }

        public UserResponse ToResponse(DateTimeOffset now) =>
            new(
                Id,
                Email,
                Name,
                Role,
                EmployeeId,
                MfaEnabled,
                MustChangePassword,
                IsDisabled,
                LockoutEnd > now ? LockoutEnd.Value.UtcDateTime : null,
                CreatedAt);
    }
}
