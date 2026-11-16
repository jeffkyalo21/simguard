using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimGuard.Api.Data;
using SimGuard.Api.Email;
using SimGuard.Api.Models;

namespace SimGuard.Api.Common.Authorization;

public enum PasswordResetStatus
{
    Done,
    InvalidLink,
    PasswordRejected
}

public sealed record PasswordResetResult(PasswordResetStatus Status, IReadOnlyList<string> PasswordErrors)
{
    public static readonly PasswordResetResult Done = new(PasswordResetStatus.Done, []);
    public static readonly PasswordResetResult InvalidLink = new(PasswordResetStatus.InvalidLink, []);
}

// Self-service password reset by emailed link. See backend/docs/auth.md.
//
// The link carries a random 32 byte token. Only its SHA-256 hash is stored.
// A request lives 30 minutes and works once; a newer request for the same
// account replaces it. Every way a link can be wrong (unknown, used, expired,
// replaced, other email) gives the same InvalidLink result, and the reason
// goes to the server log only. Identity does the actual password change, so
// the password rules, the security stamp rotation (which ends every session)
// and the lockout and MFA state behave as for any other password change: a
// reset never clears a lockout and never touches MFA.
public sealed class PasswordResetService(
    SimGuardDbContext db,
    UserManager<AppUser> users,
    TimeProvider clock,
    IEmailSender emailSender,
    IOptions<BrevoOptions> brevo,
    PasswordResetEmailLimiter emailLimiter,
    AuthEventLogger events,
    IHostEnvironment environment,
    ILogger<PasswordResetService> logger)
{
    public const int TokenBytes = 32;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private const int MaxTokenLength = 200;
    private const int RowsConsidered = 20;

    // Compared against when no real request exists, so the work done is alike.
    private static readonly byte[] DummyHash = SHA256.HashData(RandomNumberGenerator.GetBytes(TokenBytes));

    // Creates a link for an existing, enabled account and emails it. Does
    // nothing visible for any other email. The caller answers the same way
    // either way and runs this in the background.
    public async Task RequestAsync(string email, ClientInfo client, CancellationToken cancellationToken)
    {
        email = email.Trim();

        if (!emailLimiter.TryAcquire(email))
        {
            logger.LogWarning("A password reset request was dropped by the per email limit.");
            return;
        }

        var user = await users.FindByEmailAsync(email);

        if (user is null || user.IsDisabled)
        {
            await events.LogAsync(AuthEventTypes.PasswordResetRequested, false, user?.Id, email, client, cancellationToken);
            return;
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = Now();

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // A new request replaces every older unused one.
            await db.PasswordResetRequests
                .Where(r => r.UserId == user.Id && r.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, now), cancellationToken);

            var old = now.AddDays(-1);
            await db.PasswordResetRequests
                .Where(r => r.ExpiresAt < old)
                .ExecuteDeleteAsync(cancellationToken);

            db.PasswordResetRequests.Add(new PasswordResetRequest
            {
                UserId = user.Id,
                TokenHash = Hash(token),
                CreatedAt = now,
                ExpiresAt = now + Lifetime
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await events.LogAsync(AuthEventTypes.PasswordResetRequested, true, user.Id, email, client, cancellationToken);
        await SendLinkAsync(user, token, cancellationToken);
    }

    public async Task<PasswordResetResult> ResetAsync(
        string email, string token, string newPassword, ClientInfo client, CancellationToken cancellationToken)
    {
        email = email.Trim();
        var user = await users.FindByEmailAsync(email);
        var now = Now();
        var presented = Hash(token);

        PasswordResetRequest? match = null;
        var reason = "unknown_user";

        if (user is not null)
        {
            var rows = await db.PasswordResetRequests
                .Where(r => r.UserId == user.Id)
                .OrderByDescending(r => r.Id)
                .Take(RowsConsidered)
                .ToListAsync(cancellationToken);

            // Every row is compared, so the time does not depend on which matches.
            foreach (var row in rows)
            {
                if (CryptographicOperations.FixedTimeEquals(row.TokenHash, presented))
                {
                    match = row;
                }
            }

            if (rows.Count == 0)
            {
                CryptographicOperations.FixedTimeEquals(DummyHash, presented);
            }

            reason = match is null ? "unknown_token"
                : match.UsedAt is not null ? "used_or_replaced"
                : match.ExpiresAt <= now ? "expired"
                : user.IsDisabled ? "disabled"
                : string.Empty;
        }
        else
        {
            CryptographicOperations.FixedTimeEquals(DummyHash, presented);
        }

        if (user is null || match is null || reason.Length > 0)
        {
            return await FailAsync(user?.Id, email, reason, client, cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Claims the link. Two requests with the same link cannot both pass.
        var claimed = await db.PasswordResetRequests
            .Where(r => r.Id == match.Id && r.UsedAt == null && r.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, now), cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await FailAsync(user.Id, email, "used_or_replaced", client, cancellationToken);
        }

        // Identity's own token is created and used inside this call; it is
        // never shown to anyone. ResetPasswordAsync applies the password rules
        // and rotates the security stamp, which ends every session.
        var identityToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, identityToken, newPassword);

        if (!result.Succeeded)
        {
            // The link stays usable so the person can choose another password.
            await transaction.RollbackAsync(cancellationToken);
            await events.LogAsync(AuthEventTypes.PasswordResetFailed, false, user.Id, email, client, cancellationToken);
            logger.LogInformation("A password reset was refused: password_rules.");
            return new PasswordResetResult(
                PasswordResetStatus.PasswordRejected,
                result.Errors.Select(e => e.Description).ToList());
        }

        if (user.MustChangePassword)
        {
            // The person has just chosen this password themselves.
            user.MustChangePassword = false;
            await users.UpdateAsync(user);
        }

        await transaction.CommitAsync(cancellationToken);
        await events.LogAsync(AuthEventTypes.PasswordResetCompleted, true, user.Id, email, client, cancellationToken);
        return PasswordResetResult.Done;
    }

    internal static byte[] Hash(string token)
    {
        var text = token.Length > MaxTokenLength ? token[..MaxTokenLength] : token;
        return SHA256.HashData(Encoding.UTF8.GetBytes(text));
    }

    private async Task<PasswordResetResult> FailAsync(
        int? userId, string email, string reason, ClientInfo client, CancellationToken cancellationToken)
    {
        await events.LogAsync(AuthEventTypes.PasswordResetFailed, false, userId, email, client, cancellationToken);
        logger.LogInformation("A password reset was refused: {Reason}.", reason);
        return PasswordResetResult.InvalidLink;
    }

    private async Task SendLinkAsync(AppUser user, string token, CancellationToken cancellationToken)
    {
        var options = brevo.Value;

        if (!Uri.TryCreate(options.SignInUrl, UriKind.Absolute, out var signIn))
        {
            logger.LogWarning("Brevo:SignInUrl is not a valid URL. A password reset email was not sent.");
            return;
        }

        // Built from the configured address only, never from the request Host.
        // The token is in the fragment: browsers do not send it to any server.
        var link = $"{signIn.GetLeftPart(UriPartial.Authority)}/reset-password"
            + $"#token={token}&email={Uri.EscapeDataString(user.Email ?? string.Empty)}";

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            logger.LogWarning("Brevo:ApiKey is not set. A password reset email was not sent.");

            if (environment.IsDevelopment())
            {
                logger.LogInformation("Development only: password reset link {Link}", link);
            }

            return;
        }

        await emailSender.SendAsync(
            user.Email!,
            user.Name,
            EmailTemplates.PasswordReset(user.Name, user.Email!, link),
            cancellationToken);
    }

    private DateTime Now()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }
}

// Limits reset emails per address so nobody can flood an inbox. Over the
// limit the request is dropped silently: the answer must not change.
public sealed class PasswordResetEmailLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public PasswordResetEmailLimiter(IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:PasswordResetEmail:PermitLimit", 3);
        var window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:PasswordResetEmail:WindowSeconds", 3600));

        _limiter = PartitionedRateLimiter.Create<string, string>(email =>
            RateLimitPartition.GetFixedWindowLimiter(
                email.Trim().ToLowerInvariant(),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 }));
    }

    public bool TryAcquire(string email)
    {
        using var lease = _limiter.AttemptAcquire(email);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}

// Runs the request step after the response was sent, so the answer takes the
// same time for every email. Tests wait with IdleAsync.
public sealed class PasswordResetDispatcher(IServiceScopeFactory scopes, ILogger<PasswordResetDispatcher> logger)
{
    private readonly ConcurrentDictionary<Task, byte> _running = new();

    public void Enqueue(string email, ClientInfo client)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PasswordResetService>()
                    .RequestAsync(email, client, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Type only: the message could contain the email.
                logger.LogError("A password reset request failed: {Error}.", ex.GetType().Name);
            }
        });

        _running[task] = 0;
        _ = task.ContinueWith(t => _running.TryRemove(t, out _), TaskScheduler.Default);
    }

    public Task IdleAsync() => Task.WhenAll(_running.Keys);
}
