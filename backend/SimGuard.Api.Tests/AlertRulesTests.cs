using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.Tests;

public sealed class AlertTypeMapTests
{
    private static string TypeOf(string reasons) => AlertTypeMap.For(JsonDocument.Parse(reasons).RootElement);

    private static string R(string feature, double value, double contribution, string direction = "up") =>
        $$"""{"feature":"{{feature}}","value":{{value}},"contribution":{{contribution}},"direction":"{{direction}}"}""";

    [Fact]
    public void Hotspot_features_mean_sharing() =>
        Assert.Equal(AlertTypes.Sharing, TypeOf($"[{R("hotspot_sum_7d", 2, 1.5)}]"));

    [Fact]
    public void Billing_features_mean_inactive_billing() =>
        Assert.Equal(AlertTypes.InactiveBilling, TypeOf($"[{R("billing_gap_mb", 3, 2)}]"));

    [Fact]
    public void Volume_above_normal_means_reselling_and_below_means_ghost_usage()
    {
        Assert.Equal(AlertTypes.Reselling, TypeOf($"[{R("total_mb", 2.4, 1)}]"));
        Assert.Equal(AlertTypes.GhostUsage, TypeOf($"[{R("total_mb", -1.8, 1)}]"));
        Assert.Equal(AlertTypes.Reselling, TypeOf($"[{R("offpeak_ratio", 1.1, 0.5)}]"));
    }

    [Fact]
    public void The_highest_precedence_risk_increasing_reason_decides()
    {
        var reasons = $"[{R("hotspot_sessions", 1, 0.2)},{R("geo_sum_7d", 3, 3)},{R("billing_ratio", 1, 0.1)},{R("total_mb", 1, 5, "down")}]";
        Assert.Equal(AlertTypes.InactiveBilling, TypeOf(reasons));
    }

    [Fact]
    public void Low_peak_volume_first_does_not_hide_reselling()
    {
        // The demo pattern for data_reselling: peak_mb below normal ranks first.
        var reselling = $"[{R("peak_mb", -0.96, 2.8)},{R("cohort_z_iso_anomaly_score", 2, 1)},{R("offpeak_ratio", 0.8, 0.9)}]";
        Assert.Equal(AlertTypes.Reselling, TypeOf(reselling));

        var ghost = $"[{R("bytes_per_session", -1.2, 2.1)},{R("peak_mb", -0.9, 1.4)},{R("offpeak_mb", -0.7, 1)}]";
        Assert.Equal(AlertTypes.GhostUsage, TypeOf(ghost));
    }

    [Fact]
    public void Values_near_normal_and_low_billing_decide_nothing()
    {
        Assert.Equal(AlertTypes.GhostUsage, TypeOf($"[{R("peak_mb", -0.9, 2)},{R("offpeak_mb", 0.04, 1)},{R("billed_mb", -1, 0.5)}]"));
        Assert.Equal(AlertTypes.Other, TypeOf($"[{R("offpeak_mb", 0.3, 1)}]"));
    }

    [Fact]
    public void Nothing_mapped_falls_back_to_other()
    {
        Assert.Equal(AlertTypes.Other, TypeOf($"[{R("geo_flag_count", 2, 1)},{R("hotspot_sessions", 2, 1, "down")}]"));
        Assert.Equal(AlertTypes.Other, TypeOf("[]"));
    }
}

// Alert rules through real scoring runs. Day n is 2025-01-01 plus n days;
// the first scorable day for a SIM with usage from day 0 is day 14.
public sealed class AlertRulesTests(TestDatabase database) : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private ScoringHarness _h = null!;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.Available) return;
        _h = await ScoringHarness.StartAsync(database);
        await _h.ResetAsync();
    }

    public async Task DisposeAsync()
    {
        if (_h is not null) await _h.DisposeAsync();
    }

    private static IEnumerable<int> Days(int from, int to) => Enumerable.Range(from, to - from + 1);

    private static int DayOf(DateOnly date) => date.DayNumber - new DateOnly(2025, 1, 1).DayNumber;

    private static readonly (string, double, double)[] Sharing = [("hotspot_sessions", 2.5, 1.4), ("peak_mb", 1, 0.2)];

    // bucket per day: c clear, e escalated, f flag; index 0 is day 14.
    private void Pattern(string buckets, double probability = 0.5) =>
        _h.Scorer = (_, date) =>
        {
            var i = DayOf(date) - 14;
            var b = i >= 0 && i < buckets.Length ? buckets[i] : 'c';
            return b switch
            {
                'f' => new ScoringHarness.FakeScore(0.95, "flag", Sharing),
                'e' => new ScoringHarness.FakeScore(probability, "escalated", Sharing),
                _ => new ScoringHarness.FakeScore(0.03, "clear", [("peak_mb", 0.1, -1)])
            };
        };

    private async Task<List<Models.Alert>> AlertsAsync()
    {
        await using var db = database.NewContext();
        return await db.Alerts.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
    }

    [DbFact]
    public async Task Three_of_five_scored_days_raise_one_alert_on_the_third_day()
    {
        var sim = await _h.AddSimAsync("field", Days(0, 25));
        Pattern("ecece");

        var run = await _h.RunAsync();

        var alert = Assert.Single(await AlertsAsync());
        Assert.Equal(1, run.AlertsCreated);
        Assert.Equal(sim, alert.SimId);
        Assert.Equal(ScoringHarness.Day(18), alert.DetectedOn);
        Assert.Equal(ScoringHarness.Day(18), alert.LastDetectedOn);
        Assert.Equal(AlertStatuses.Escalated, alert.Status);
        Assert.Equal(RiskLevels.Medium, alert.RiskLevel);
        Assert.Equal(AlertTypes.Sharing, alert.Type);
        Assert.Equal(0.5m, alert.RiskScore);
        Assert.Equal("hotspot_sessions", alert.Reasons.RootElement[0].GetProperty("feature").GetString());
        Assert.Equal("up", alert.Reasons.RootElement[0].GetProperty("direction").GetString());
    }

    [DbFact]
    public async Task Two_of_five_raise_nothing()
    {
        await _h.AddSimAsync("field", Days(0, 25));
        Pattern("ecceccecce");   // at most 2 of any 5 consecutive scored days

        var run = await _h.RunAsync();

        Assert.Equal(0, run.AlertsCreated);
        Assert.Empty(await AlertsAsync());
    }

    [DbFact]
    public async Task A_backfill_leaves_one_open_alert_per_sustained_sim()
    {
        var sim = await _h.AddSimAsync("field", Days(0, 30));
        var other = await _h.AddSimAsync("support", Days(0, 30));
        Pattern("eeeeeeeefeeeeeee", probability: 0.4);

        var run = await _h.RunAsync();

        var alerts = await AlertsAsync();
        Assert.Equal(2, alerts.Count);
        Assert.Equal(2, run.AlertsCreated);
        Assert.Equal(2 * 13, run.AlertsUpdated);
        Assert.All(alerts, a =>
        {
            Assert.Equal(ScoringHarness.Day(16), a.DetectedOn);
            Assert.Equal(ScoringHarness.Day(29), a.LastDetectedOn);
            Assert.Equal(AlertStatuses.Flagged, a.Status);   // upgraded on the flag day
            Assert.Equal(RiskLevels.High, a.RiskLevel);
            Assert.Equal(0.95m, a.RiskScore);
        });
        Assert.Equal([sim, other], alerts.Select(a => a.SimId).Order().ToArray());

        var again = await _h.RunAsync();
        var forced = await _h.RunAsync(force: true);

        Assert.Equal(0, again.AlertsCreated);
        Assert.Equal(0, forced.AlertsCreated);
        Assert.Equal(2, (await AlertsAsync()).Count);
    }

    [DbFact]
    public async Task A_false_positive_starts_a_seven_day_cooldown()
    {
        await _h.AddSimAsync("field", Days(0, 40));
        Pattern(new string('e', 30));
        await _h.RunAsync(to: ScoringHarness.Day(20));
        await database.ExecuteAsync(
            "UPDATE alerts SET status = 'cleared', resolution = 'falsePositive', resolved_at = (SELECT max(created_at) FROM risk_scores)");

        var run = await _h.RunAsync();

        var alerts = await AlertsAsync();
        Assert.Equal(2, alerts.Count);
        Assert.Equal(1, run.AlertsCreated);
        Assert.Equal(AlertStatuses.Cleared, alerts[0].Status);
        Assert.Equal(ScoringHarness.Day(20), alerts[0].LastDetectedOn);
        Assert.Equal(ScoringHarness.Day(28), alerts[1].DetectedOn);   // 21..27 in cooldown
        Assert.Equal(AlertStatuses.Escalated, alerts[1].Status);
    }

    [DbFact]
    public async Task A_confirmed_misuse_alert_is_never_reopened()
    {
        await _h.AddSimAsync("field", Days(0, 30));
        Pattern(new string('e', 20));
        await _h.RunAsync(to: ScoringHarness.Day(20));
        await database.ExecuteAsync(
            "UPDATE alerts SET status = 'cleared', resolution = 'confirmedMisuse', resolved_at = (SELECT max(created_at) FROM risk_scores)");

        await _h.RunAsync(force: true, to: ScoringHarness.Day(20));
        Assert.Single(await AlertsAsync());   // replaying reviewed days adds nothing

        await _h.RunAsync();

        var alerts = await AlertsAsync();
        Assert.Equal(2, alerts.Count);
        Assert.Equal(AlertStatuses.Cleared, alerts[0].Status);
        Assert.Equal(AlertResolutions.ConfirmedMisuse, alerts[0].Resolution);
        Assert.Equal(ScoringHarness.Day(21), alerts[1].DetectedOn);
    }
}
