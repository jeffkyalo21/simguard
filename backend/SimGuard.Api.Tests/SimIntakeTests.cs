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

// What the SIM intake screens rely on: a SIM whose ICCID is already in the
// system cannot be added again, and an allocation for a SIM that does not
// exist is refused. Both run through the real HTTP pipeline on a throwaway
// PostgreSQL database, and both must leave the tables as they were.
public sealed class SimIntakeTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string Iccid = "8925401000000000011";

    [DbFact]
    public async Task A_SIM_with_an_ICCID_that_already_exists_is_refused_with_409_and_creates_no_row()
    {
        await using var app = new Harness(database);
        var carrierId = await app.SeedCarrierAsync($"Test carrier {Guid.NewGuid().ToString("N")[..12]}");
        await app.SeedSimAsync(carrierId, "+254712000001", Iccid);
        var before = await CountsAsync();

        var response = await app.Client.PostAsJsonAsync("/api/sims", new
        {
            phoneNumber = "+254712000002",
            iccid = Iccid,
            carrierId,
            planType = "field",
            monthlyCost = 0
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("errors").GetProperty("iccid")[0].GetString();
        Assert.Equal($"A SIM with ICCID '{Iccid}' already exists.", message);

        Assert.Equal(before, await CountsAsync());
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sims WHERE phone_number = '+254712000002'"));
    }

    [DbFact]
    public async Task An_allocation_for_a_SIM_that_does_not_exist_is_refused_with_400_and_writes_nothing()
    {
        await using var app = new Harness(database);
        var employeeId = await app.SeedEmployeeAsync("intake@example.test");
        var before = await CountsAsync();

        var response = await app.Client.PostAsJsonAsync("/api/allocations", new
        {
            simId = 987654,
            employeeId,
            assignedBy = employeeId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("errors").GetProperty("simId")[0].GetString();
        Assert.Equal("SIM 987654 does not exist.", message);

        Assert.Equal(before, await CountsAsync());
    }

    // Rows in the tables a refused request must leave alone. The class shares
    // one database, so each test compares with its own starting point.
    private async Task<(long Sims, long Allocations, long Audit)> CountsAsync() => (
        await database.ScalarAsync("SELECT COUNT(*) FROM sims"),
        await database.ScalarAsync("SELECT COUNT(*) FROM allocations"),
        await database.ScalarAsync("SELECT COUNT(*) FROM audit_logs"));

    // One app per test on the class's database, with a Security Officer signed in and
    // no email sender that could reach anyone.
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
            var email = $"intake-officer-{Guid.NewGuid():N}@example.test";
            var user = new AppUser
            {
                UserName = email,
                Email = email,
                Name = "Intake Officer",
                CreatedAt = DateTime.UtcNow
            };

            Assert.True(users.CreateAsync(user, "intake password 12345").GetAwaiter().GetResult().Succeeded);
            Assert.True(users.AddToRoleAsync(user, AppRoles.SecurityOfficer).GetAwaiter().GetResult().Succeeded);
            Assert.True(users.ResetAuthenticatorKeyAsync(user).GetAwaiter().GetResult().Succeeded);
            Assert.True(users.SetTwoFactorEnabledAsync(user, true).GetAwaiter().GetResult().Succeeded);
            return scope.ServiceProvider.GetRequiredService<TokenService>().CreateAccessToken(user, AppRoles.SecurityOfficer).Token;
        }

        public async Task<int> SeedCarrierAsync(string name)
        {
            await using var db = _database.NewContext();
            var carrier = new Carrier { Name = name, CreatedAt = DateTime.UtcNow };
            db.Carriers.Add(carrier);
            await db.SaveChangesAsync();
            return carrier.Id;
        }

        public async Task SeedSimAsync(int carrierId, string phoneNumber, string iccid)
        {
            await using var db = _database.NewContext();
            db.Sims.Add(new Sim
            {
                PhoneNumber = phoneNumber,
                Iccid = iccid,
                CarrierId = carrierId,
                PlanType = "field",
                MonthlyCost = 0,
                Status = SimStatuses.Available,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        public async Task<int> SeedEmployeeAsync(string email)
        {
            await using var db = _database.NewContext();
            var department = new Department { Name = "Field operations" };
            db.Employees.Add(new Employee
            {
                Name = "Intake Person",
                Email = email,
                Department = department,
                JobRole = "Technician",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return db.Employees.Single(e => e.Email == email).Id;
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
