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

// The alert screens tell a confirmed misuse from an alert dismissed as normal
// use. That needs the resolution on list items, a resolution filter on the
// list, and the dashboard's resolved count split in two that adds up.
public sealed class AlertOutcomeTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [DbFact]
    public async Task The_alert_list_returns_the_resolution_and_filters_by_it()
    {
        await using var app = new Harness(database);
        var simId = await app.SeedSimAsync();
        await app.SeedAlertAsync(simId, AlertStatuses.Flagged, null);
        await app.SeedAlertAsync(simId, AlertStatuses.Cleared, AlertResolutions.ConfirmedMisuse);
        await app.SeedAlertAsync(simId, AlertStatuses.Cleared, AlertResolutions.FalsePositive);

        var all = await app.ListAsync($"simId={simId}");
        Assert.Equal(3, all.Count);
        Assert.Equal(
            new string?[] { null, AlertResolutions.ConfirmedMisuse, AlertResolutions.FalsePositive },
            all.Select(a => a.Resolution).OrderBy(r => r, StringComparer.Ordinal).ToArray());
        Assert.Equal(AlertStatuses.Flagged, Assert.Single(all, a => a.Resolution is null).Status);

        var confirmed = await app.ListAsync($"simId={simId}&status=cleared&resolution=confirmedMisuse");
        Assert.Equal(AlertResolutions.ConfirmedMisuse, Assert.Single(confirmed).Resolution);

        var dismissed = await app.ListAsync($"simId={simId}&status=cleared&resolution=falsePositive");
        Assert.Equal(AlertResolutions.FalsePositive, Assert.Single(dismissed).Resolution);

        var bad = await app.Client.GetAsync("/api/alerts?resolution=maybe");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [DbFact]
    public async Task The_dashboard_splits_resolved_into_confirmed_and_dismissed_and_they_add_up()
    {
        await using var app = new Harness(database);
        var simId = await app.SeedSimAsync();
        var before = await app.SummaryAsync();

        await app.SeedAlertAsync(simId, AlertStatuses.Cleared, AlertResolutions.ConfirmedMisuse);
        await app.SeedAlertAsync(simId, AlertStatuses.Cleared, AlertResolutions.ConfirmedMisuse);
        await app.SeedAlertAsync(simId, AlertStatuses.Cleared, AlertResolutions.FalsePositive);
        // A cleared alert without a resolution counts as dismissed.
        await app.SeedAlertAsync(simId, AlertStatuses.Cleared, null);
        await app.SeedAlertAsync(simId, AlertStatuses.Flagged, null);

        var after = await app.SummaryAsync();

        Assert.Equal(before.Confirmed + 2, after.Confirmed);
        Assert.Equal(before.Dismissed + 2, after.Dismissed);
        Assert.Equal(before.Resolved + 4, after.Resolved);
        Assert.Equal(after.Resolved, after.Confirmed + after.Dismissed);
    }

    private sealed record ListedAlert(int Id, string Status, string? Resolution);

    private sealed record Summary(int Resolved, int Confirmed, int Dismissed);

    // One app per test on the class's database, with a Security Officer signed
    // in and no email sender that could reach anyone.
    private sealed class Harness : IAsyncDisposable
    {
        private readonly TestDatabase _database;
        private readonly WebApplicationFactory<Program> _factory;

        public Harness(TestDatabase database)
        {
            _database = database;

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

            Client = _factory.CreateClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SignInOfficer());
        }

        public HttpClient Client { get; }

        private string SignInOfficer()
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            // Unique, because the tests of this class share one database.
            var email = $"outcome-officer-{Guid.NewGuid():N}@example.test";
            var user = new AppUser
            {
                UserName = email,
                Email = email,
                Name = "Outcome Officer",
                CreatedAt = DateTime.UtcNow
            };

            Assert.True(users.CreateAsync(user, "outcome password 12345").GetAwaiter().GetResult().Succeeded);
            Assert.True(users.AddToRoleAsync(user, AppRoles.SecurityOfficer).GetAwaiter().GetResult().Succeeded);
            Assert.True(users.ResetAuthenticatorKeyAsync(user).GetAwaiter().GetResult().Succeeded);
            Assert.True(users.SetTwoFactorEnabledAsync(user, true).GetAwaiter().GetResult().Succeeded);
            return scope.ServiceProvider.GetRequiredService<TokenService>().CreateAccessToken(user, AppRoles.SecurityOfficer).Token;
        }

        public async Task<int> SeedSimAsync()
        {
            await using var db = _database.NewContext();
            var unique = Guid.NewGuid().ToString("N");
            var sim = new Sim
            {
                PhoneNumber = $"+2547{Random.Shared.Next(10000000, 99999999)}",
                Iccid = $"89{unique}"[..21],
                Carrier = new Carrier { Name = $"Carrier {unique[..12]}", CreatedAt = DateTime.UtcNow },
                PlanType = "field",
                MonthlyCost = 0,
                Status = SimStatuses.Available,
                CreatedAt = DateTime.UtcNow
            };
            db.Sims.Add(sim);
            await db.SaveChangesAsync();
            return sim.Id;
        }

        public async Task SeedAlertAsync(int simId, string status, string? resolution)
        {
            await using var db = _database.NewContext();
            var now = DateTime.UtcNow;
            db.Alerts.Add(new Alert
            {
                SimId = simId,
                Type = AlertTypes.Sharing,
                RiskScore = 0.9m,
                RiskLevel = RiskLevels.High,
                Status = status,
                CreatedAt = now,
                ResolvedAt = status == AlertStatuses.Cleared ? now : null,
                Resolution = resolution
            });
            await db.SaveChangesAsync();
        }

        public async Task<List<ListedAlert>> ListAsync(string query)
        {
            var response = await Client.GetAsync($"/api/alerts?{query}&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => new ListedAlert(
                    item.GetProperty("id").GetInt32(),
                    item.GetProperty("status").GetString()!,
                    item.GetProperty("resolution").GetString()))
                .ToList();
        }

        public async Task<Summary> SummaryAsync()
        {
            var response = await Client.GetAsync("/api/dashboard/summary?days=30");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = body.RootElement;
            return new Summary(
                root.GetProperty("resolved").GetInt32(),
                root.GetProperty("resolvedConfirmed").GetInt32(),
                root.GetProperty("resolvedDismissed").GetInt32());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
        }
    }

    private sealed class NoEmail : IEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string toName, EmailMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
