using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SimGuard.CarrierMock.Tests;

public sealed class EndpointTests : IDisposable
{
    private const string AdminKey = "admin-key-0123456789abcdef";

    private readonly TestData _data = new(10);
    private readonly List<IDisposable> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories)
        {
            f.Dispose();
        }

        _data.Dispose();
    }

    private HttpClient Client(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["CarrierMock:DataDir"] = _data.Dir,
            ["CarrierMock:SessionsFile"] = Path.Combine(_data.Dir, "none.csv"),
            ["CarrierMock:Keys:CarrierA"] = TestData.KeyA,
            ["CarrierMock:Keys:CarrierB"] = TestData.KeyB,
            ["MOCK_START_DAY"] = "3",
            ["MOCK_SCHEDULER_ENABLED"] = "false",
            ["MOCK_STATE_FILE"] = _data.StateFile,
            ["MOCK_ADMIN_KEY"] = AdminKey
        };

        foreach (var (k, v) in extra ?? [])
        {
            settings[k] = v;
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            foreach (var (k, v) in settings)
            {
                b.UseSetting(k, v);
            }

            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FakeClock(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero))));
        });

        _factories.Add(factory);
        return factory.CreateClient();
    }

    private static HttpRequestMessage Get(string url, string? apiKey = TestData.KeyA)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (apiKey is not null)
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        return request;
    }

    private static HttpRequestMessage Admin(HttpMethod method, string url, string? adminKey = AdminKey, string? apiKey = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (adminKey is not null)
        {
            request.Headers.Add("X-Admin-Key", adminKey);
        }

        if (apiKey is not null)
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Feed_serves_released_days_only()
    {
        var client = Client();

        var released = await client.SendAsync(Get("/v1/usage/summary?date=2025-01-03"));
        var held = await client.SendAsync(Get("/v1/usage/summary?date=2025-01-04"));

        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal(2, (await Json(released)).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, held.StatusCode);
        Assert.Equal("date_not_available", (await Json(held)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Manual_release_makes_the_next_day_available()
    {
        var client = Client();

        var response = await client.SendAsync(Admin(HttpMethod.Post, "/admin/release-next-day"));
        var body = await Json(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("released", body.GetProperty("status").GetString());
        Assert.Equal(4, body.GetProperty("releasedThroughDay").GetInt32());
        Assert.Equal("2025-01-04", body.GetProperty("releasedThroughDate").GetString());
        Assert.Equal(6, body.GetProperty("daysLeft").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/v1/usage/summary?date=2025-01-04"))).StatusCode);
    }

    [Fact]
    public async Task Status_shows_pointer_next_release_and_days_left()
    {
        var client = Client();

        var body = await Json(await client.SendAsync(Admin(HttpMethod.Get, "/admin/status")));

        Assert.Equal(3, body.GetProperty("releasedThroughDay").GetInt32());
        Assert.Equal(7, body.GetProperty("daysLeft").GetInt32());
        Assert.Equal(10, body.GetProperty("totalDays").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextReleaseAt").ValueKind);   // scheduler off in this test
    }

    [Fact]
    public async Task End_of_data_returns_the_last_day_with_no_more_days()
    {
        var client = Client(new() { ["MOCK_START_DAY"] = "10" });

        var body = await Json(await client.SendAsync(Admin(HttpMethod.Post, "/admin/release-next-day")));
        var after = await client.SendAsync(Get("/v1/usage/summary?date=2025-01-11"));

        Assert.Equal("no more days", body.GetProperty("status").GetString());
        Assert.Equal(10, body.GetProperty("releasedThroughDay").GetInt32());
        Assert.Equal("2025-01-10", body.GetProperty("releasedThroughDate").GetString());
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        Assert.Contains("no more days", (await Json(after)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Reset_pointer_goes_back_to_the_start_day_or_a_given_day()
    {
        var client = Client();
        await client.SendAsync(Admin(HttpMethod.Post, "/admin/release-next-day"));

        var toStart = await Json(await client.SendAsync(Admin(HttpMethod.Post, "/admin/reset-pointer", body: new { })));
        var toSix = await Json(await client.SendAsync(Admin(HttpMethod.Post, "/admin/reset-pointer", body: new { day = 6 })));
        var negative = await client.SendAsync(Admin(HttpMethod.Post, "/admin/reset-pointer", body: new { day = -1 }));

        Assert.Equal(3, toStart.GetProperty("releasedThroughDay").GetInt32());
        Assert.Equal(6, toSix.GetProperty("releasedThroughDay").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("wrong-admin-key", null)]
    [InlineData(null, TestData.KeyA)]          // a carrier key is not enough once MOCK_ADMIN_KEY is set
    public async Task Admin_routes_need_the_admin_key(string? adminKey, string? apiKey)
    {
        var client = Client();

        foreach (var (method, url) in new[]
                 {
                     (HttpMethod.Get, "/admin/status"),
                     (HttpMethod.Post, "/admin/release-next-day"),
                     (HttpMethod.Post, "/admin/reset-pointer"),
                     (HttpMethod.Get, "/admin/state"),
                     (HttpMethod.Post, "/admin/advance-day"),
                     (HttpMethod.Post, "/admin/fault"),
                     (HttpMethod.Post, "/admin/reset")
                 })
        {
            var response = await client.SendAsync(Admin(method, url, adminKey, apiKey));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // Nothing moved.
        var status = await Json(await client.SendAsync(Admin(HttpMethod.Get, "/admin/status")));
        Assert.Equal(3, status.GetProperty("releasedThroughDay").GetInt32());
    }

    [Fact]
    public async Task Without_an_admin_key_a_carrier_key_opens_the_admin_routes()
    {
        var client = Client(new() { ["MOCK_ADMIN_KEY"] = "" });

        var withCarrierKey = await client.SendAsync(Admin(HttpMethod.Post, "/admin/release-next-day", adminKey: null, apiKey: TestData.KeyB));
        var withNothing = await client.SendAsync(Admin(HttpMethod.Post, "/admin/release-next-day", adminKey: null));

        Assert.Equal(HttpStatusCode.OK, withCarrierKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withNothing.StatusCode);
    }

    [Fact]
    public async Task Feed_needs_a_carrier_key_and_each_carrier_sees_its_own_sims()
    {
        var client = Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/v1/usage/summary?date=2025-01-01", apiKey: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/v1/usage/summary?date=2025-01-01", apiKey: AdminKey))).StatusCode);

        var b = await Json(await client.SendAsync(Get("/v1/usage/summary?date=2025-01-01", TestData.KeyB)));
        var iccids = b.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("iccid").GetString()).ToList();
        Assert.Equal([TestData.Iccid(3), TestData.Iccid(4)], iccids);
    }

    [Fact]
    public async Task Date_shift_serves_shifted_dates_with_the_same_weekday()
    {
        var client = Client(new() { ["MOCK_DATE_SHIFT_DAYS"] = "7" });

        var shifted = await Json(await client.SendAsync(Get("/v1/usage/summary?date=2025-01-08")));
        var held = await client.SendAsync(Get("/v1/usage/summary?date=2025-01-11"));
        var status = await Json(await client.SendAsync(Admin(HttpMethod.Get, "/admin/status")));

        Assert.Equal("2025-01-08", shifted.GetProperty("date").GetString());
        Assert.Equal("2025-01-08", shifted.GetProperty("items")[0].GetProperty("date").GetString());
        Assert.Equal(15, shifted.GetProperty("items")[0].GetProperty("dataMb").GetDouble());   // the data of 2025-01-01
        Assert.Equal(HttpStatusCode.NotFound, held.StatusCode);
        Assert.Equal("2025-01-10", status.GetProperty("releasedThroughDate").GetString());
        Assert.Equal("2025-01-03", status.GetProperty("releasedThroughDataDate").GetString());
    }

    [Fact]
    public async Task Pointer_is_kept_in_the_state_file_across_restarts()
    {
        var first = Client();
        await first.SendAsync(Admin(HttpMethod.Post, "/admin/release-next-day"));

        var second = Client();
        var status = await Json(await second.SendAsync(Admin(HttpMethod.Get, "/admin/status")));

        Assert.Equal(4, status.GetProperty("releasedThroughDay").GetInt32());
    }

    [Fact]
    public async Task Health_page_needs_no_key_and_shows_no_secret()
    {
        var client = Client();

        var response = await client.GetAsync("/");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(TestData.KeyA, text);
        Assert.DoesNotContain(AdminKey, text);
    }
}
