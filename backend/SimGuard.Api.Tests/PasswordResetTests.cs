using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Email;
using SimGuard.Api.Models;

namespace SimGuard.Api.Tests;

// The forgot and reset password flow through the real HTTP pipeline on a
// throwaway PostgreSQL database: the same answers for every email, the token
// rules (30 minutes, single use, replaced by a newer request, hash only), and
// what a reset does and does not change (sessions end, MFA and lockout stay).
public sealed class PasswordResetTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string OldPassword = "old password 12345";
    private const string NewPassword = "brand new password 678";
    private const string Generic = "This link is invalid or has expired.";

    [DbFact]
    public async Task Forgot_password_gives_the_same_answer_for_unknown_disabled_and_valid_emails()
    {
        await using var app = new Harness(database);
        await app.AddUserAsync("valid@example.test");
        await app.AddUserAsync("disabled@example.test", disabled: true);

        var answers = new List<(HttpStatusCode, string)>();

        foreach (var email in new[] { "nobody@example.test", "disabled@example.test", "valid@example.test" })
        {
            var response = await app.Client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
            answers.Add((response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        await app.Dispatcher.IdleAsync();

        Assert.All(answers, a => Assert.Equal(HttpStatusCode.OK, a.Item1));
        Assert.Single(answers.Select(a => a.Item2).Distinct());
        Assert.Contains("If that account exists", answers[0].Item2);

        // Only the existing, enabled account got an email.
        var sent = Assert.Single(app.Email.Sent);
        Assert.Equal("valid@example.test", sent.To);
        Assert.Contains("valid for 30 minutes", sent.Message.Text);
        Assert.Contains("If you did not ask for this", sent.Message.Text);
        Assert.DoesNotContain(OldPassword, sent.Message.Text);
        Assert.StartsWith("https://app.example.test/reset-password#token=", Regex.Match(sent.Message.Text, @"https://\S+").Value);
    }

    [DbFact]
    public async Task Link_stops_working_after_30_minutes()
    {
        await using var app = new Harness(database);
        await app.AddUserAsync("late@example.test");
        var token = await app.RequestLinkAsync("late@example.test");

        app.Clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));
        var late = await app.ResetAsync("late@example.test", token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        Assert.Contains(Generic, await late.Content.ReadAsStringAsync());

        // A fresh link just before the limit still works.
        var fresh = await app.RequestLinkAsync("late@example.test");
        app.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await app.ResetAsync("late@example.test", fresh, NewPassword)).StatusCode);
    }

    [DbFact]
    public async Task Link_works_once()
    {
        await using var app = new Harness(database);
        await app.AddUserAsync("once@example.test");
        var token = await app.RequestLinkAsync("once@example.test");

        Assert.Equal(HttpStatusCode.OK, (await app.ResetAsync("once@example.test", token, NewPassword)).StatusCode);

        var again = await app.ResetAsync("once@example.test", token, "another password 999");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains(Generic, await again.Content.ReadAsStringAsync());
    }

    [DbFact]
    public async Task A_second_request_replaces_the_first_link()
    {
        await using var app = new Harness(database);
        await app.AddUserAsync("twice@example.test");
        var first = await app.RequestLinkAsync("twice@example.test");
        var second = await app.RequestLinkAsync("twice@example.test");

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.ResetAsync("twice@example.test", first, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.ResetAsync("twice@example.test", second, NewPassword)).StatusCode);
    }

    [DbFact]
    public async Task Wrong_token_wrong_email_and_unknown_email_all_give_the_same_400()
    {
        await using var app = new Harness(database);
        await app.AddUserAsync("owner@example.test");
        await app.AddUserAsync("other@example.test");
        var token = await app.RequestLinkAsync("owner@example.test");

        var bodies = new List<string>();

        foreach (var (email, sent) in new[]
        {
            ("owner@example.test", "not-the-token"),
            ("other@example.test", token),
            ("nobody@example.test", token),
            ("owner@example.test", ""),
        })
        {
            var response = await app.ResetAsync(email, sent, NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        Assert.All(bodies, b => Assert.Contains(Generic, b));
        Assert.Single(bodies.Select(Strip).Distinct());

        // The right pair still works afterwards: wrong guesses do not burn the link.
        Assert.Equal(HttpStatusCode.OK, (await app.ResetAsync("owner@example.test", token, NewPassword)).StatusCode);

        static string Strip(string body) => Regex.Replace(body, "\"traceId\":\"[^\"]*\"", string.Empty);
    }

    [DbFact]
    public async Task After_a_reset_the_old_password_fails_and_MFA_is_still_required()
    {
        await using var app = new Harness(database);
        await app.AddUserAsync("mfa@example.test");
        var token = await app.RequestLinkAsync("mfa@example.test");

        Assert.Equal(HttpStatusCode.OK, (await app.ResetAsync("mfa@example.test", token, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.LoginAsync("mfa@example.test", OldPassword)).StatusCode);

        var login = await app.LoginAsync("mfa@example.test", NewPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"mfaRequired\"", body);
        Assert.DoesNotContain("accessToken", body);

        var user = await app.FindAsync("mfa@example.test");
        Assert.True(user.TwoFactorEnabled);
    }

    [DbFact]
    public async Task A_reset_ends_existing_sessions()
    {
        await using var app = new Harness(database);
        var id = await app.AddUserAsync("session@example.test");
        var access = app.AccessTokenFor(id);

        using var before = app.Authorized(access, "/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, (await app.Client.SendAsync(before)).StatusCode);

        var token = await app.RequestLinkAsync("session@example.test");
        Assert.Equal(HttpStatusCode.OK, (await app.ResetAsync("session@example.test", token, NewPassword)).StatusCode);

        using var after = app.Authorized(access, "/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.SendAsync(after)).StatusCode);
    }

    [DbFact]
    public async Task A_reset_does_not_clear_a_lockout()
    {
        await using var app = new Harness(database);
        await app.AddUserAsync("locked@example.test");
        var token = await app.RequestLinkAsync("locked@example.test");

        await app.LockAsync("locked@example.test");
        var lockedUntil = (await app.FindAsync("locked@example.test")).LockoutEnd;
        Assert.Equal(HttpStatusCode.OK, (await app.ResetAsync("locked@example.test", token, NewPassword)).StatusCode);

        var user = await app.FindAsync("locked@example.test");
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.Equal(lockedUntil, user.LockoutEnd);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.LoginAsync("locked@example.test", NewPassword)).StatusCode);
    }

    [DbFact]
    public async Task A_weak_password_is_refused_and_the_link_stays_usable()
    {
        await using var app = new Harness(database);
        await app.AddUserAsync("weak@example.test");
        var token = await app.RequestLinkAsync("weak@example.test");

        var weak = await app.ResetAsync("weak@example.test", token, "short");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("newPassword", await weak.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await app.ResetAsync("weak@example.test", token, NewPassword)).StatusCode);
    }

    [DbFact]
    public async Task The_rate_limit_per_ip_answers_429()
    {
        await using var app = new Harness(database, ("RateLimiting:PasswordReset:PermitLimit", "3"));

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await app.Client.PostAsJsonAsync("/api/auth/forgot-password", new { email = $"ip{i}@example.test" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [DbFact]
    public async Task The_rate_limit_per_email_stops_inbox_flooding_without_changing_the_answer()
    {
        await using var app = new Harness(database, ("RateLimiting:PasswordResetEmail:PermitLimit", "2"));
        await app.AddUserAsync("flood@example.test");

        var bodies = new List<string>();

        for (var i = 0; i < 5; i++)
        {
            var response = await app.Client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "FLOOD@example.test" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
            await app.Dispatcher.IdleAsync();
        }

        Assert.Single(bodies.Distinct());
        Assert.Equal(2, app.Email.Sent.Count(s => s.To == "flood@example.test"));
    }

    [DbFact]
    public async Task The_token_is_stored_only_as_a_hash_and_never_logged()
    {
        await using var app = new Harness(database);
        var id = await app.AddUserAsync("hash@example.test");
        var token = await app.RequestLinkAsync("hash@example.test");
        Assert.Equal(HttpStatusCode.OK, (await app.ResetAsync("hash@example.test", token, NewPassword)).StatusCode);
        await app.ResetAsync("hash@example.test", "a wrong token", NewPassword);

        // 32 random bytes, URL safe.
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);

        await using var db = database.NewContext();
        var rows = db.PasswordResetRequests.Where(r => r.UserId == id).ToList();
        var row = Assert.Single(rows);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)), row.TokenHash);
        Assert.Equal(32, row.TokenHash.Length);
        Assert.Equal(TimeSpan.FromMinutes(30), row.ExpiresAt - row.CreatedAt);
        Assert.NotNull(row.UsedAt);

        // Nowhere in the security log either.
        var events = db.AuthEvents.Where(e => e.UserId == id).ToList();
        Assert.Contains(events, e => e.EventType == AuthEventTypes.PasswordResetRequested && e.Success);
        Assert.Contains(events, e => e.EventType == AuthEventTypes.PasswordResetCompleted && e.Success);
        Assert.Contains(events, e => e.EventType == AuthEventTypes.PasswordResetFailed && !e.Success);
        Assert.DoesNotContain(events, e => (e.EmailAttempted + e.UserAgent + e.IpAddress).Contains(token));

        var log = string.Join("\n", app.Logs.Lines);
        Assert.DoesNotContain(token, log);
        Assert.DoesNotContain(NewPassword, log);
        Assert.DoesNotContain("reset-password#", log);
    }

    // One app per test: its own clock, email recorder and log sink, on the
    // class's database.
    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;

        public Harness(TestDatabase database, params (string Key, string Value)[] settings)
        {
            Clock = new FakeClock(DateTimeOffset.UtcNow);
            Email = new RecordingEmail();
            Logs = new LogSink();

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Development");
                host.UseSetting("ConnectionStrings:DefaultConnection", database.ConnectionString);
                host.UseSetting("Jwt:Key", "test-signing-key-test-signing-key-0123456789");
                host.UseSetting("Brevo:ApiKey", "test-key-not-real");
                host.UseSetting("Brevo:SignInUrl", "https://app.example.test/login");
                host.UseSetting("Scoring:Enabled", "false");
                host.UseSetting("RateLimiting:Auth:PermitLimit", "1000");

                foreach (var (key, value) in settings)
                {
                    host.UseSetting(key, value);
                }

                host.ConfigureServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(Clock);
                    services.RemoveAll<IEmailSender>();
                    services.AddSingleton<IEmailSender>(Email);
                });
                host.ConfigureLogging(logging => logging.AddProvider(Logs));
            });

            Client = _factory.CreateClient();
        }

        public FakeClock Clock { get; }
        public RecordingEmail Email { get; }
        public LogSink Logs { get; }
        public HttpClient Client { get; }
        public PasswordResetDispatcher Dispatcher => _factory.Services.GetRequiredService<PasswordResetDispatcher>();

        public async Task<int> AddUserAsync(string email, bool disabled = false)
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser
            {
                UserName = email,
                Email = email,
                Name = "Test Person",
                IsDisabled = disabled,
                CreatedAt = DateTime.UtcNow
            };

            Assert.True((await users.CreateAsync(user, OldPassword)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, AppRoles.Admin)).Succeeded);
            Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            return user.Id;
        }

        public async Task<AppUser> FindAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            return (await users.FindByEmailAsync(email))!;
        }

        public async Task LockAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByEmailAsync(email))!;

            for (var i = 0; i < 5; i++)
            {
                await users.AccessFailedAsync(user);
            }

            Assert.True(await users.IsLockedOutAsync(user));
        }

        public string AccessTokenFor(int id)
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = users.FindByIdAsync(id.ToString()).GetAwaiter().GetResult()!;
            return scope.ServiceProvider.GetRequiredService<TokenService>().CreateAccessToken(user, AppRoles.Admin).Token;
        }

        public HttpRequestMessage Authorized(string accessToken, string path)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new("Bearer", accessToken);
            return request;
        }

        // Asks for a link and returns the token from the email that was sent.
        public async Task<string> RequestLinkAsync(string email)
        {
            var before = Email.Sent.Count;
            var response = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await Dispatcher.IdleAsync();

            var message = Email.Sent.Skip(before).Single(s => s.To == email).Message.Text;
            return Regex.Match(message, "#token=([A-Za-z0-9_-]+)&").Groups[1].Value;
        }

        public Task<HttpResponseMessage> ResetAsync(string email, string token, string password) =>
            Client.PostAsJsonAsync("/api/auth/reset-password", new { email, token, newPassword = password });

        public Task<HttpResponseMessage> LoginAsync(string email, string password) =>
            Client.PostAsJsonAsync("/api/auth/login", new { email, password });

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
        }
    }

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class RecordingEmail : IEmailSender
    {
        public ConcurrentQueue<(string To, EmailMessage Message)> SentQueue { get; } = new();
        public List<(string To, EmailMessage Message)> Sent => SentQueue.ToList();

        public Task<bool> SendAsync(string toEmail, string toName, EmailMessage message, CancellationToken cancellationToken = default)
        {
            SentQueue.Enqueue((toEmail, message));
            return Task.FromResult(true);
        }
    }

    private sealed class LogSink : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _lines = new();
        public IReadOnlyCollection<string> Lines => _lines.ToArray();

        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _lines.Enqueue(formatter(state, exception));

        public void Dispose()
        {
        }
    }
}
