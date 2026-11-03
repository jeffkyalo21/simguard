using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Models;

namespace SimGuard.Api.Data;

// Runs once at startup. Creates the three roles and, when there are no
// accounts yet, the first Admin from SimGuard:Seed:AdminEmail and
// SimGuard:Seed:AdminPassword. The password is never logged.
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

        if (await userManager.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var email = configuration["SimGuard:Seed:AdminEmail"]?.Trim();
        var password = configuration["SimGuard:Seed:AdminPassword"];

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            logger.LogWarning(
                "There are no user accounts. Set SimGuard:Seed:AdminEmail and SimGuard:Seed:AdminPassword " +
                "(user-secrets or environment variables) and restart to create the first Admin.");
            return;
        }

        var admin = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Name = configuration["SimGuard:Seed:AdminName"]?.Trim() is { Length: > 0 } name
                ? name
                : "Administrator",
            MustChangePassword = true,
            CreatedAt = Timestamps.UtcNow()
        };

        var result = await userManager.CreateAsync(admin, password);

        if (!result.Succeeded)
        {
            logger.LogError(
                "Could not create the first Admin: {Errors}",
                string.Join(" ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, AppRoles.Admin);
        await events.LogAsync(AuthEventTypes.UserCreated, true, admin.Id, email, cancellationToken);

        logger.LogInformation(
            "Created the first Admin account. The first sign-in requires a password change and MFA enrolment.");
    }
}
