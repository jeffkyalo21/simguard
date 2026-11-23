using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Email;
using SimGuard.Api.Models;

namespace SimGuard.Api.Tests;

// A deleted account keeps its row (marked by the placeholder email) but is not
// a user any more: not in the list, the role counts or by id; its old email is
// free again; its audit entries still show "Deleted user N"; its sessions end.
// The delete goes through the real endpoint, with a real password and code.
public sealed class DeletedUsersTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string Password = "deleted users password 12345";

    [DbFact]
    public async Task The_list_hides_a_deleted_user_and_keeps_active_and_not_yet_signed_in_users()
    {
        await using var app = new Harness(database);
        var active = await app.CreateUserAsync(AppRoles.SecurityOfficer);
        var invited = await app.CreateUserAsync(AppRoles.Auditor, mustChangePassword: true);
        var leaver = await app.CreateUserAsync(AppRoles.SecurityOfficer);

        await app.DeleteAccountAsync(leaver);

        var emails = await app.ListEmailsAsync();
        Assert.Contains(active.Email, emails);
        Assert.Contains(invited.Email, emails);
        Assert.DoesNotContain(emails, e => DeletedAccounts.IsDeletedEmail(e));
        Assert.DoesNotContain(leaver.Email, emails);
        // The row is still there for the audit trail.
        Assert.Equal(1, await database.ScalarAsync($"SELECT COUNT(*) FROM app_users WHERE id = {leaver.Id}"));

        // The role filter (the role dropdown) hides it too, and so does a read by id.
        var officers = await app.ListEmailsAsync("?role=SecurityOfficer&pageSize=100");
        Assert.DoesNotContain(officers, e => DeletedAccounts.IsDeletedEmail(e));
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync($"/api/users/{leaver.Id}")).StatusCode);
    }

    [DbFact]
    public async Task The_role_counts_on_system_health_do_not_count_a_deleted_user()
    {
        await using var app = new Harness(database);
        var before = await app.RoleCountsAsync(AppRoles.Auditor);
        var leaver = await app.CreateUserAsync(AppRoles.Auditor);
        Assert.Equal(before.Active + 1, (await app.RoleCountsAsync(AppRoles.Auditor)).Active);

        await app.DeleteAccountAsync(leaver);

        Assert.Equal(before, await app.RoleCountsAsync(AppRoles.Auditor));
    }

    [DbFact]
    public async Task A_deleted_users_old_email_can_be_invited_again()
    {
        await using var app = new Harness(database);
        var leaver = await app.CreateUserAsync(AppRoles.SecurityOfficer);
        var oldEmail = leaver.Email;

        Assert.Equal(HttpStatusCode.Conflict, (await app.InviteAsync(oldEmail)).StatusCode);

        await app.DeleteAccountAsync(leaver);

        Assert.Equal(HttpStatusCode.Created, (await app.InviteAsync(oldEmail)).StatusCode);
        Assert.Contains(oldEmail, await app.ListEmailsAsync("?pageSize=100"));
    }

    [DbFact]
    public async Task An_invite_cannot_use_the_deleted_account_domain()
    {
        await using var app = new Harness(database);

        var response = await app.InviteAsync("deleted-user-1@deleted.invalid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("email", await response.Content.ReadAsStringAsync());
    }

    [DbFact]
    public async Task An_audit_entry_by_a_deleted_user_still_shows_as_Deleted_user_N()
    {
        await using var app = new Harness(database);
        var leaver = await app.CreateUserAsync(AppRoles.SecurityOfficer);
        await database.ExecuteAsync($"""
            INSERT INTO audit_logs (actor_user_id, actor_email, actor_role, action, category, timestamp)
            VALUES ({leaver.Id}, '{leaver.Email}', 'SecurityOfficer', 'user.role_changed', 'system', now())
            """);

        await app.DeleteAccountAsync(leaver);

        using var body = JsonDocument.Parse(await app.Client.GetStringAsync("/api/audit-logs?pageSize=100"));
        var mine = body.RootElement.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("actorUserId").ValueKind == JsonValueKind.Number && i.GetProperty("actorUserId").GetInt32() == leaver.Id)
            .ToList();

        Assert.True(mine.Count >= 2, "the old entry and the deletion entry are both listed");
        Assert.All(mine, i => Assert.Equal($"Deleted user {leaver.Id}", i.GetProperty("actorEmail").GetString()));
    }

    [DbFact]
    public async Task A_deleted_users_session_stops_working()
    {
        await using var app = new Harness(database);
        var leaver = await app.CreateUserAsync(AppRoles.SecurityOfficer);
        using var asLeaver = app.ClientFor(leaver);
        Assert.Equal(HttpStatusCode.OK, (await asLeaver.GetAsync("/api/auth/me")).StatusCode);

        await app.DeleteAccountAsync(leaver);

        Assert.Equal(HttpStatusCode.Unauthorized, (await asLeaver.GetAsync("/api/auth/me")).StatusCode);
    }

    // RFC 6238: the 6-digit code the authenticator app shows now (30 s steps, SHA-1).
    private static string CurrentCode(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32Key.TrimEnd('=').ToUpperInvariant().Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var step = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30).Reverse().ToArray();
        var hash = System.Security.Cryptography.HMACSHA1.HashData(key, step);
        var offset = hash[^1] & 0x0F;
        var number = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (number % 1_000_000).ToString("D6");
    }

    private sealed record Account(int Id, string Email, string Token);

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;

        public Harness(TestDatabase database)
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Development");
                host.UseSetting("ConnectionStrings:DefaultConnection", database.ConnectionString);
                host.UseSetting("Jwt:Key", "test-signing-key-test-signing-key-0123456789");
                host.UseSetting("Brevo:ApiKey", "test-key-not-real");
                host.UseSetting("Scoring:Enabled", "false");
                host.UseSetting("RateLimiting:Auth:PermitLimit", "1000");

                host.ConfigureServices(services =>
                {
                    services.RemoveAll<IEmailSender>();
                    services.AddSingleton<IEmailSender, NoEmail>();
                });
            });

            var admin = CreateUserAsync(AppRoles.Admin).GetAwaiter().GetResult();
            Client = ClientFor(admin);
        }

        public HttpClient Client { get; }

        public HttpClient ClientFor(Account account)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
            return client;
        }

        public async Task<Account> CreateUserAsync(string role, bool mustChangePassword = false)
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"deleted-users-{Guid.NewGuid():N}@example.test";
            var user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                Name = "Test Person",
                MustChangePassword = mustChangePassword,
                CreatedAt = DateTime.UtcNow
            };

            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            var token = scope.ServiceProvider.GetRequiredService<TokenService>().CreateAccessToken(user, role).Token;
            return new Account(user.Id, email, token);
        }

        // The real endpoint, with the real password and a current authenticator code.
        public async Task DeleteAccountAsync(Account account)
        {
            string code;

            using (var scope = _factory.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var user = (await users.FindByIdAsync(account.Id.ToString()))!;
                code = CurrentCode((await users.GetAuthenticatorKeyAsync(user))!);
            }

            using var client = ClientFor(account);
            var response = await client.PostAsJsonAsync("/api/auth/me/delete", new { password = Password, code });
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        public async Task<List<string>> ListEmailsAsync(string query = "?pageSize=100")
        {
            using var body = JsonDocument.Parse(await Client.GetStringAsync($"/api/users{query}"));
            return body.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("email").GetString()!).ToList();
        }

        public Task<HttpResponseMessage> InviteAsync(string email) =>
            Client.PostAsJsonAsync("/api/users", new { email, name = "Invited Person", role = AppRoles.Auditor });

        public async Task<(int Active, int Disabled)> RoleCountsAsync(string role)
        {
            using var body = JsonDocument.Parse(await Client.GetStringAsync("/api/admin/system-status"));
            var row = body.RootElement.GetProperty("usersByRole").EnumerateArray().Single(r => r.GetProperty("role").GetString() == role);
            return (row.GetProperty("active").GetInt32(), row.GetProperty("disabled").GetInt32());
        }

        public ValueTask DisposeAsync() => _factory.DisposeAsync();
    }

    private sealed class NoEmail : IEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string toName, EmailMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
