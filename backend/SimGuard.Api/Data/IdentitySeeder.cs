using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Endpoints;
using SimGuard.Api.Models;

namespace SimGuard.Api.Data;

// Runs once at startup. Creates the three roles and, when there are no
// accounts yet, the first Admin from SimGuard:Seed:AdminEmail and
// SimGuard:Seed:AdminPassword.
//
// In Development only, it also creates one SecurityOfficer and one Auditor
// for local testing when SimGuard:Seed:OfficerEmail/OfficerPassword and
// SimGuard:Seed:AuditorEmail/AuditorPassword are set (user-secrets or
// environment variables) and no account has that email yet. There are no
// default passwords. Every seeded account must change its password and
// enrol MFA at first sign-in. Passwords are never logged.
//
// Emergency recovery for the server owner: when SimGuard:Seed:RecoverAdmin is
// true together with SimGuard:Seed:AdminEmail and SimGuard:Seed:AdminPassword,
// that account is made an enabled, unlocked Admin with MFA reset and a forced
// password change (see RecoverAdminAsync). It runs only here, at startup.
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        try
        {
            await SeedCoreAsync(provider, logger, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The API still starts so /health/database can report the problem.
            logger.LogError(
                "Identity seeding skipped: {Error}. Check that the database is reachable and migrations are applied.",
                exception.GetType().Name);
        }
    }

    private static async Task SeedCoreAsync(
        IServiceProvider provider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<int>>>();
        var userManager = provider.GetRequiredService<UserManager<AppUser>>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var events = provider.GetRequiredService<AuthEventLogger>();
        var environment = provider.GetRequiredService<IHostEnvironment>();

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var created = await roleManager.CreateAsync(new IdentityRole<int>(role));

                if (!created.Succeeded)
                {
                    throw new InvalidOperationException($"Could not create role {role}.");
                }
            }
        }

        var recovered = string.Equals(
            configuration["SimGuard:Seed:RecoverAdmin"]?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            && await RecoverAdminAsync(provider, userManager, configuration, events, logger, cancellationToken);

        if (!recovered && !await userManager.Users.AnyAsync(cancellationToken))
        {
            await SeedFirstAdminAsync(userManager, configuration, events, logger, cancellationToken);
        }

        if (environment.IsDevelopment())
        {
            await SeedDevUserAsync(
                userManager, configuration, events, logger, "Officer", AppRoles.SecurityOfficer,
                "Security Officer", cancellationToken);
            await SeedDevUserAsync(
                userManager, configuration, events, logger, "Auditor", AppRoles.Auditor,
                "Auditor", cancellationToken);
        }
    }

    // Returns true when the recovery ran (account created or repaired).
    private static async Task<bool> RecoverAdminAsync(
        IServiceProvider provider,
        UserManager<AppUser> userManager,
        IConfiguration configuration,
        AuthEventLogger events,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var email = configuration["SimGuard:Seed:AdminEmail"]?.Trim();
        var password = configuration["SimGuard:Seed:AdminPassword"];

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            logger.LogError(
                "SimGuard:Seed:RecoverAdmin is true but SimGuard:Seed:AdminEmail or SimGuard:Seed:AdminPassword " +
                "is missing. Nothing was changed.");
            return false;
        }

        logger.LogWarning(
            "ADMIN RECOVERY is on (SimGuard:Seed:RecoverAdmin=true). Remove SimGuard__Seed__RecoverAdmin and " +
            "SimGuard__Seed__AdminPassword from the server settings and restart as soon as you have signed in.");

        var db = provider.GetRequiredService<SimGuardDbContext>();
        var audit = provider.GetRequiredService<AuditWriter>();
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            if (!await CreateUserAsync(
                    userManager, events, logger, email, password, "Administrator", AppRoles.Admin, cancellationToken))
            {
                return false;
            }

            audit.AddSystem(AuditActions.AdminRecovered, null, null, new { email, outcome = "created" });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Admin recovery: created the Admin account {Email}.", email);
            return true;
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var previousRoles = await userManager.GetRolesAsync(user);
            var wasDisabled = user.IsDisabled;
            var wasLocked = user.LockoutEnd > DateTimeOffset.UtcNow;

            var others = previousRoles.Where(role => role != AppRoles.Admin).ToList();
            if (others.Count > 0)
            {
                Ensure(await userManager.RemoveFromRolesAsync(user, others));
            }

            if (!previousRoles.Contains(AppRoles.Admin))
            {
                Ensure(await userManager.AddToRoleAsync(user, AppRoles.Admin));
            }

            user.IsDisabled = false;
            user.EmailConfirmed = true;
            user.MustChangePassword = true;
            Ensure(await userManager.UpdateAsync(user));

            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            Ensure(await userManager.ResetPasswordAsync(user, resetToken, password));
            Ensure(await userManager.SetLockoutEndDateAsync(user, null));
            Ensure(await userManager.ResetAccessFailedCountAsync(user));
            Ensure(await userManager.SetTwoFactorEnabledAsync(user, false));
            Ensure(await userManager.ResetAuthenticatorKeyAsync(user));
            Ensure(await AuthEndpoints.ClearRecoveryCodesAsync(userManager, user));
            Ensure(await userManager.UpdateSecurityStampAsync(user));

            // No password, hash or secret in the entry.
            audit.AddSystem(
                AuditActions.AdminRecovered,
                null,
                new { email, roles = previousRoles, isDisabled = wasDisabled, locked = wasLocked },
                new { email, outcome = "repaired", role = AppRoles.Admin, isDisabled = false, locked = false, mfaReset = true, mustChangePassword = true });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await events.LogAsync(AuthEventTypes.MfaReset, true, user.Id, user.Email, cancellationToken);
            logger.LogWarning("Admin recovery: repaired the Admin account {Email}.", email);
            return true;
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("Admin recovery failed", StringComparison.Ordinal))
        {
            // The transaction is rolled back: the account is exactly as it was.
            logger.LogError("{Error} Nothing was changed.", exception.Message);
            return false;
        }
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            // Descriptions never contain the password. The outer catch logs the type only.
            throw new InvalidOperationException(
                "Admin recovery failed: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task SeedFirstAdminAsync(
        UserManager<AppUser> userManager,
        IConfiguration configuration,
        AuthEventLogger events,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var email = configuration["SimGuard:Seed:AdminEmail"]?.Trim();
        var password = configuration["SimGuard:Seed:AdminPassword"];

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            logger.LogWarning(
                "There are no user accounts. Set SimGuard:Seed:AdminEmail and SimGuard:Seed:AdminPassword " +
                "(user-secrets or environment variables) and restart to create the first Admin.");
            return;
        }

        var name = configuration["SimGuard:Seed:AdminName"]?.Trim() is { Length: > 0 } adminName
            ? adminName
            : "Administrator";

        if (await CreateUserAsync(
                userManager, events, logger, email, password, name, AppRoles.Admin, cancellationToken))
        {
            logger.LogInformation(
                "Created the first Admin account. The first sign-in requires a password change and MFA enrolment.");
        }
    }

    // prefix is "Officer" or "Auditor": reads SimGuard:Seed:{prefix}Email,
    // {prefix}Password and the optional {prefix}Name.
    private static async Task SeedDevUserAsync(
        UserManager<AppUser> userManager,
        IConfiguration configuration,
        AuthEventLogger events,
        ILogger logger,
        string prefix,
        string role,
        string defaultName,
        CancellationToken cancellationToken)
    {
        var email = configuration[$"SimGuard:Seed:{prefix}Email"]?.Trim();
        var password = configuration[$"SimGuard:Seed:{prefix}Password"];

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            return;
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var name = configuration[$"SimGuard:Seed:{prefix}Name"]?.Trim() is { Length: > 0 } configured
            ? configured
            : defaultName;

        if (await CreateUserAsync(userManager, events, logger, email, password, name, role, cancellationToken))
        {
            logger.LogInformation(
                "Development: created the {Role} test account. The first sign-in requires a password change and MFA enrolment.",
                role);
        }
    }

    private static async Task<bool> CreateUserAsync(
        UserManager<AppUser> userManager,
        AuthEventLogger events,
        ILogger logger,
        string email,
        string password,
        string name,
        string role,
        CancellationToken cancellationToken)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Name = name,
            MustChangePassword = true,
            CreatedAt = Timestamps.UtcNow()
        };

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            logger.LogError(
                "Could not create the seeded {Role} account: {Errors}",
                role,
                string.Join(" ", result.Errors.Select(e => e.Description)));
            return false;
        }

        await userManager.AddToRoleAsync(user, role);
        await events.LogAsync(AuthEventTypes.UserCreated, true, user.Id, email, cancellationToken);
        return true;
    }
}
