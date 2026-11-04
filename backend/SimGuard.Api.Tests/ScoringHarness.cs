using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Ml;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.Tests;

// Fake ML scorer plus the real scoring services against a TestDatabase.
public sealed class ScoringHarness : IAsyncDisposable
{
    public sealed record FakeScore(double Probability, string Bucket, (string Feature, double Value, double Contribution)[] Reasons);

    public TestDatabase Db { get; }
    public FakeMlServer Ml { get; }
    public ServiceProvider Services { get; }

    // Score per SIM and as-of date. Default: by SIM id modulo 3.
    public Func<int, DateOnly, FakeScore> Scorer { get; set; } = (sim, _) => (sim % 3) switch
    {
        0 => new FakeScore(0.95, "flag", [("hotspot_sessions", 3, 2.1), ("peak_mb", 1.2, 0.5)]),
        1 => new FakeScore(0.5, "escalated", [("peak_mb", -0.9, 0.8)]),
        _ => new FakeScore(0.05, "clear", [("peak_mb", 0.1, -1.0)])
    };

    // Return this status for /v1/score on these dates instead of scoring.
    public Dictionary<DateOnly, int> FailOn { get; } = new();

    public bool VersionDown { get; set; }

    public List<(DateOnly Date, int[] Sims)> ScoreCalls { get; } = [];

    private ScoringHarness(TestDatabase db, FakeMlServer ml, ServiceProvider services)
    {
        Db = db;
        Ml = ml;
        Services = services;
    }

    public static async Task<ScoringHarness> StartAsync(TestDatabase db, Dictionary<string, string?>? settings = null)
    {
        var ml = await FakeMlServer.StartAsync();
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = db.ConnectionString,
            ["MlService:BaseUrl"] = ml.BaseUrl,
            ["MlService:SharedSecret"] = "harness-secret",
            ["MlService:TimeoutSeconds"] = "10",
            ["MlService:RetryDelaySeconds"] = "0.01",
            ["Scoring:BatchSize"] = "1000"
        };

        foreach (var (k, v) in settings ?? [])
        {
            values[k] = v;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<SimGuardDbContext>(o => o.UseNpgsql(db.ConnectionString));
        services.AddHttpContextAccessor();
        services.AddScoped<AuditWriter>();
        services.AddSingleton(TimeProvider.System);
        services.AddMlServiceClient(configuration);
        services.AddScoringPipeline(configuration);

        var harness = new ScoringHarness(db, ml, services.BuildServiceProvider());
        ml.Handler = harness.HandleAsync;
        return harness;
    }

    private async Task HandleAsync(HttpContext ctx, string body)
    {
        if (ctx.Request.Path == "/version")
        {
            if (VersionDown)
            {
                await FakeMlServer.Json(ctx, 503, """{"title":"Service Unavailable","status":503,"detail":"down"}""");
                return;
            }

            await FakeMlServer.Json(ctx, 200, """
                {"service":"simguard-ml","apiVersion":"v1","models":[{"file":"xgboost_triage.joblib","sizeBytes":1,"sha256":"ab"}],"libraries":{}}
                """);
            return;
        }

        if (ctx.Request.Path == "/v1/score")
        {
            using var doc = JsonDocument.Parse(body);
            var date = DateOnly.ParseExact(doc.RootElement.GetProperty("asOfDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var sims = doc.RootElement.GetProperty("simIds").EnumerateArray().Select(e => e.GetInt32()).ToArray();

            lock (ScoreCalls)
            {
                ScoreCalls.Add((date, sims));
            }

            if (FailOn.TryGetValue(date, out var status))
            {
                await FakeMlServer.Json(ctx, status, $$"""{"title":"Error","status":{{status}},"detail":"fake failure"}""");
                return;
            }

            var results = sims.Select(sim =>
            {
                var s = Scorer(sim, date);
                return new
                {
                    simId = sim,
                    date = date.ToString("yyyy-MM-dd"),
                    probability = s.Probability,
                    bucket = s.Bucket,
                    reasons = s.Reasons.Select(r => new
                    {
                        feature = r.Feature,
                        value = r.Value,
                        contribution = r.Contribution,
                        direction = r.Contribution > 0 ? "increases_risk" : "decreases_risk"
                    }),
                    similarCases = Array.Empty<object>()
                };
            });

            await FakeMlServer.Json(ctx, 200, JsonSerializer.Serialize(new
            {
                asOfDate = date.ToString("yyyy-MM-dd"),
                results,
                notFound = Array.Empty<int>()
            }));
            return;
        }

        ctx.Response.StatusCode = 404;
    }

    // Adds a carrier (once) and one SIM with usage on the given days,
    // counted from 2025-01-01 (day 0). Returns the SIM id.
    public async Task<int> AddSimAsync(string tier, IEnumerable<int> days)
    {
        var suffix = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);
        await Db.ExecuteAsync("INSERT INTO carriers (name) VALUES ('Test Carrier') ON CONFLICT DO NOTHING");
        var simId = (int)await Db.ScalarAsync($"""
            INSERT INTO sims (phone_number, iccid, plan_type, monthly_cost, carrier_id, tier, status)
            SELECT '+2547{suffix % 100_000_000:00000000}', '89254{suffix:00000000000}000', '{tier}', 0,
                   (SELECT id FROM carriers WHERE name = 'Test Carrier'), '{tier}', 'available'
            RETURNING id
            """);

        var rows = new StringBuilder();

        foreach (var d in days)
        {
            if (rows.Length > 0) rows.Append(',');
            var date = new DateOnly(2025, 1, 1).AddDays(d);
            rows.Append($"({simId}, '{date:yyyy-MM-dd}', 100, 10, 2, 600, 0, 0, 1, 0, 60, 50, 110, 0, 110, '{tier}', 3, 2, 100, 50)");
        }

        if (rows.Length > 0)
        {
            await Db.ExecuteAsync($"""
                INSERT INTO daily_usage (sim_id, date, total_download_mb, total_upload_mb, data_session_count,
                    data_duration_s, hotspot_sessions, public_apn_sessions, unique_towers, geo_flag_count, peak_mb,
                    offpeak_mb, total_mb, roaming_sessions, billed_mb, tier, voice_minutes_used, sms_count,
                    voice_minutes_remaining, sms_remaining)
                VALUES {rows}
                """);
        }

        return simId;
    }

    public static DateOnly Day(int d) => new DateOnly(2025, 1, 1).AddDays(d);

    public async Task<int> EnqueueAsync(bool force = false, int[]? sims = null, DateOnly? from = null, DateOnly? to = null,
        string trigger = ScoringTriggers.Manual)
    {
        var queue = Services.GetRequiredService<ScoringQueue>();
        var result = await queue.EnqueueAsync(
            new ScoringRequest(trigger, force, sims, from, to, null, "tester@example.com"),
            CancellationToken.None);
        return result.RunId;
    }

    // Takes and executes queued runs like ScoringWorker does.
    public async Task<Models.ScoringRun> RunAsync(bool force = false, int[]? sims = null, DateOnly? from = null, DateOnly? to = null,
        string trigger = ScoringTriggers.Manual)
    {
        var id = await EnqueueAsync(force, sims, from, to, trigger);

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
            var taken = await Services.GetRequiredService<ScoringQueue>().TakeNextAsync(db, CancellationToken.None);
            Assert.Equal(id, taken);
            await scope.ServiceProvider.GetRequiredService<ScoringRunner>().RunAsync(id, CancellationToken.None);
        }

        await using var read = Db.NewContext();
        return await read.ScoringRuns.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    public async Task ResetAsync()
    {
        await Db.ExecuteAsync("""
            TRUNCATE alerts, risk_scores, daily_usage, scoring_runs, audit_logs RESTART IDENTITY CASCADE;
            DELETE FROM sims;
            """);
        ScoreCalls.Clear();
        FailOn.Clear();
        VersionDown = false;
    }

    public async ValueTask DisposeAsync()
    {
        await Ml.DisposeAsync();
        await Services.DisposeAsync();
    }
}
