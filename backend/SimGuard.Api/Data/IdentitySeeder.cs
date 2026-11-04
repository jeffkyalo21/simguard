using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
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

        if (!await userManager.Users.AnyAsync(cancellationToken))
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
