using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

// "Handed over by" (allocations.assigned_by) is optional: an allocation can be
// made, read, listed, imported and its holder deleted without it, and when it
// is sent it is still checked. All through the real HTTP pipeline.
public sealed class HandedOverByTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [DbFact]
    public async Task Allocating_without_assignedBy_returns_201_and_stores_null()
    {
        await using var app = new Harness(database);
        var simId = await app.SeedSimAsync();
        var holder = await app.SeedEmployeeAsync();

        var response = await app.Client.PostAsJsonAsync("/api/allocations", new { simId, employeeId = holder });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("assignedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("assignedByName").ValueKind);
        var id = body.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(1, await database.ScalarAsync($"SELECT COUNT(*) FROM allocations WHERE id = {id} AND assigned_by IS NULL"));
    }

    [DbFact]
    public async Task Allocating_with_a_valid_assignedBy_stores_it_and_returns_the_name()
    {
        await using var app = new Harness(database);
        var simId = await app.SeedSimAsync();
        var holder = await app.SeedEmployeeAsync();
        var giver = await app.SeedEmployeeAsync("Giver Person");

        var response = await app.Client.PostAsJsonAsync("/api/allocations", new { simId, employeeId = holder, assignedBy = giver });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(giver, body.RootElement.GetProperty("assignedBy").GetInt32());
        Assert.Equal("Giver Person", body.RootElement.GetProperty("assignedByName").GetString());
    }

    [DbFact]
    public async Task An_unknown_or_non_positive_assignedBy_is_refused_with_400_and_writes_nothing()
    {
        await using var app = new Harness(database);
        var simId = await app.SeedSimAsync();
        var holder = await app.SeedEmployeeAsync();
        var before = await database.ScalarAsync("SELECT COUNT(*) FROM allocations");

        var unknown = await app.Client.PostAsJsonAsync("/api/allocations", new { simId, employeeId = holder, assignedBy = 987654 });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using (var body = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Employee 987654 does not exist.", body.RootElement.GetProperty("errors").GetProperty("assignedBy")[0].GetString());
        }

        var zero = await app.Client.PostAsJsonAsync("/api/allocations", new { simId, employeeId = holder, assignedBy = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        Assert.Equal(before, await database.ScalarAsync("SELECT COUNT(*) FROM allocations"));
    }

    [DbFact]
    public async Task An_allocation_without_a_handover_can_be_read_listed_and_returned()
    {
        await using var app = new Harness(database);
        var simId = await app.SeedSimAsync();
        var holder = await app.SeedEmployeeAsync();
        var id = await app.AllocateAsync(simId, holder, null);

        var one = await app.GetJsonAsync($"/api/allocations/{id}");
        Assert.Equal(JsonValueKind.Null, one.GetProperty("assignedBy").ValueKind);

        var listed = await app.GetJsonAsync($"/api/allocations?simId={simId}&pageSize=100");
        var item = Assert.Single(listed.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("assignedByName").ValueKind);

        var history = await app.GetJsonAsync($"/api/employees/{holder}/allocations");
        Assert.NotEqual(0, history.GetProperty("items").GetArrayLength());

        var closed = await app.Client.PostAsync($"/api/allocations/{id}/return", null);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
    }

    [DbFact]
    public async Task The_SIM_activity_drops_the_by_part_when_nobody_is_recorded()
    {
        await using var app = new Harness(database);
        var simWithout = await app.SeedSimAsync();
        var simWith = await app.SeedSimAsync();
        var holder = await app.SeedEmployeeAsync("Holder Person");
        var giver = await app.SeedEmployeeAsync("Giver Person");
        await app.AllocateAsync(simWithout, holder, null);
        await app.AllocateAsync(simWith, holder, giver);

        var without = await app.StartedSummaryAsync(simWithout);
        Assert.StartsWith("Assigned to Holder Person (", without);
        Assert.EndsWith(")", without);
        Assert.DoesNotContain(" by ", without);

        Assert.EndsWith(" by Giver Person", await app.StartedSummaryAsync(simWith));
    }

    [DbFact]
    public async Task Employee_delete_still_protects_holders_and_assigners_and_allows_others()
    {
        await using var app = new Harness(database);
        var simId = await app.SeedSimAsync();
        var otherSim = await app.SeedSimAsync();
        var holderOfNull = await app.SeedEmployeeAsync();
        var holder = await app.SeedEmployeeAsync();
        var giver = await app.SeedEmployeeAsync();
        var bystander = await app.SeedEmployeeAsync();
        await app.AllocateAsync(simId, holderOfNull, null);
        await app.AllocateAsync(otherSim, holder, giver);

        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.DeleteAsync($"/api/employees/{holderOfNull}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Client.DeleteAsync($"/api/employees/{giver}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Client.DeleteAsync($"/api/employees/{bystander}")).StatusCode);
    }

    [DbFact]
    public async Task The_CSV_import_stores_null_without_a_linked_employee_and_the_importers_employee_with_one()
    {
        await using var app = new Harness(database);
        var holder = await app.SeedEmployeeAsync();
        var simA = await app.SeedSimAsync();

        Assert.Equal(HttpStatusCode.OK, await app.ImportAllocationAsync(simA, holder));
        Assert.Equal(1, await database.ScalarAsync($"SELECT COUNT(*) FROM allocations WHERE sim_id = {simA} AND assigned_by IS NULL"));

        var importerEmployee = await app.SeedEmployeeAsync("Importer Person");
        await using var linked = new Harness(database, importerEmployee);
        var simB = await linked.SeedSimAsync();

        Assert.Equal(HttpStatusCode.OK, await linked.ImportAllocationAsync(simB, holder, app.EmailOf(holder)));
        Assert.Equal(1, await database.ScalarAsync($"SELECT COUNT(*) FROM allocations WHERE sim_id = {simB} AND assigned_by = {importerEmployee}"));
    }

    // One app per test on the class's database, with a Security Officer signed in
    // (optionally linked to an employee) and no email sender that could reach anyone.
    private sealed class Harness : IAsyncDisposable
    {
        private static readonly Dictionary<int, string> Emails = [];

        private readonly TestDatabase _database;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly Dictionary<int, string> _iccids = [];

        public Harness(TestDatabase database, int? linkedEmployeeId = null)
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
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SignInOfficer(linkedEmployeeId));
        }

        public HttpClient Client { get; }

        public string EmailOf(int employeeId) => Emails[employeeId];

        private string SignInOfficer(int? employeeId)
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"handover-officer-{Guid.NewGuid():N}@example.test";
            var user = new AppUser
            {
                UserName = email,
                Email = email,
                Name = "Handover Officer",
                EmployeeId = employeeId,
                CreatedAt = DateTime.UtcNow
            };

            Assert.True(users.CreateAsync(user, "handover password 12345").GetAwaiter().GetResult().Succeeded);
            Assert.True(users.AddToRoleAsync(user, AppRoles.SecurityOfficer).GetAwaiter().GetResult().Succeeded);
            Assert.True(users.ResetAuthenticatorKeyAsync(user).GetAwaiter().GetResult().Succeeded);
            Assert.True(users.SetTwoFactorEnabledAsync(user, true).GetAwaiter().GetResult().Succeeded);
            return scope.ServiceProvider.GetRequiredService<TokenService>().CreateAccessToken(user, AppRoles.SecurityOfficer).Token;
        }

        public async Task<int> SeedSimAsync()
        {
            await using var db = _database.NewContext();
            var unique = Guid.NewGuid().ToString("N");
            // Digits only, as the CSV import requires.
            var iccid = $"89{Random.Shared.NextInt64(100_000_000_000_000_000, 999_999_999_999_999_999)}";
            var sim = new Sim
            {
                PhoneNumber = $"+2547{Random.Shared.Next(10000000, 99999999)}",
                Iccid = iccid,
                Carrier = new Carrier { Name = $"Carrier {unique[..12]}", CreatedAt = DateTime.UtcNow },
                PlanType = "field",
                MonthlyCost = 0,
                Status = SimStatuses.Available,
                CreatedAt = DateTime.UtcNow
            };
            db.Sims.Add(sim);
            await db.SaveChangesAsync();
            _iccids[sim.Id] = iccid;
            return sim.Id;
        }

        public async Task<int> SeedEmployeeAsync(string? name = null)
        {
            await using var db = _database.NewContext();
            var email = $"handover-{Guid.NewGuid():N}@example.test";
            var employee = new Employee
            {
                Name = name ?? "Handover Person",
                Email = email,
                Department = new Department { Name = $"Dept {Guid.NewGuid():N}"[..20] },
                JobRole = "Technician",
                CreatedAt = DateTime.UtcNow
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            Emails[employee.Id] = email;
            return employee.Id;
        }

        public async Task<int> AllocateAsync(int simId, int employeeId, int? assignedBy)
        {
            var response = await Client.PostAsJsonAsync("/api/allocations", new { simId, employeeId, assignedBy });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("id").GetInt32();
        }

        public async Task<JsonElement> GetJsonAsync(string url)
        {
            var response = await Client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.Clone();
        }

        public async Task<string> StartedSummaryAsync(int simId)
        {
            var activity = await GetJsonAsync($"/api/sims/{simId}/activity");
            return activity.GetProperty("items").EnumerateArray()
                .First(i => i.GetProperty("kind").GetString() == "allocationStarted")
                .GetProperty("summary").GetString()!;
        }

        public async Task<HttpStatusCode> ImportAllocationAsync(int simId, int employeeId, string? email = null)
        {
            var csv = $"iccid,employee_email,start_date\n{_iccids[simId]},{email ?? Emails[employeeId]},\n";
            using var form = new MultipartFormDataContent
            {
                { new StringContent(csv, Encoding.UTF8, "text/csv"), "file", "allocations.csv" }
            };
            var response = await Client.PostAsync("/api/imports/allocations?mode=commit", form);
            if (!response.IsSuccessStatusCode) Assert.Fail(await response.Content.ReadAsStringAsync());
            return response.StatusCode;
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
