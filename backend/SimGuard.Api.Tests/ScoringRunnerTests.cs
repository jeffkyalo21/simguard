using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.Tests;

// Scoring runs against a real PostgreSQL database and a fake ML service.
// Day n means 2025-01-01 plus n days; a SIM is scorable on day n when it
// has usage on the 15 days ending on day n.
public sealed class ScoringRunnerTests(TestDatabase database) : IClassFixture<TestDatabase>, IAsyncLifetime
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

    private async Task<long> ScoreCountAsync() => await database.ScalarAsync("SELECT count(*) FROM risk_scores");

    [DbFact]
    public async Task Scores_only_scorable_sim_days_in_ascending_date_order()
    {
        var full = await _h.AddSimAsync("field", Days(0, 19));             // days 14..19
        var tooShort = await _h.AddSimAsync("field", Days(0, 13));         // none
        var gap = await _h.AddSimAsync("support", Days(0, 9).Concat(Days(11, 30))); // days 25..30

        var run = await _h.RunAsync();

        Assert.Equal(ScoringRunStatuses.Succeeded, run.Status);
        Assert.Equal(12, run.SimDaysScored);
        Assert.Equal(12, run.DatesScored);
        Assert.Equal(ScoringHarness.Day(14), run.FirstDate);
        Assert.Equal(ScoringHarness.Day(30), run.LastDate);
        Assert.StartsWith("v1-", run.ModelVersion);
        Assert.Equal(12, await ScoreCountAsync());

        var dates = _h.ScoreCalls.Select(c => c.Date).ToList();
        Assert.Equal(dates.Order().ToList(), dates);
        Assert.Equal(dates.Distinct().Count(), dates.Count);
        Assert.DoesNotContain(_h.ScoreCalls, c => c.Sims.Contains(tooShort));
        Assert.All(_h.ScoreCalls.Where(c => c.Date <= ScoringHarness.Day(19)), c => Assert.Equal([full], c.Sims));
        Assert.All(_h.ScoreCalls.Where(c => c.Date > ScoringHarness.Day(19)), c => Assert.Equal([gap], c.Sims));
        Assert.Equal(0, await database.ScalarAsync(
            $"SELECT count(*) FROM risk_scores WHERE sim_id = {gap} AND date < '2025-01-26'"));
    }

    [DbFact]
    public async Task Stores_score_bucket_reasons_and_model_version()
    {
        var sim = await _h.AddSimAsync("field", Days(0, 14));
        _h.Scorer = (_, _) => new ScoringHarness.FakeScore(0.93456, "flag",
            [("hotspot_sessions", 2.5, 1.2), ("peak_mb", -0.3, -0.4)]);

        var run = await _h.RunAsync();

        await using var db = database.NewContext();
        var score = await db.RiskScores.AsNoTracking().SingleAsync();
        Assert.Equal(sim, score.SimId);
        Assert.Equal(ScoringHarness.Day(14), score.Date);
        Assert.Equal(0.9346m, score.Probability);
        Assert.Equal("flag", score.Bucket);
        Assert.Equal(run.ModelVersion, score.ModelVersion);
        var reasons = score.Reasons.RootElement.EnumerateArray().ToList();
        Assert.Equal("hotspot_sessions", reasons[0].GetProperty("feature").GetString());
        Assert.Equal("up", reasons[0].GetProperty("direction").GetString());
        Assert.Equal("down", reasons[1].GetProperty("direction").GetString());
    }

    [DbFact]
    public async Task Repeating_a_run_creates_no_duplicates_and_sends_nothing()
    {
        await _h.AddSimAsync("field", Days(0, 19));
        await _h.AddSimAsync("manager", Days(0, 19));
        await _h.RunAsync();
        _h.ScoreCalls.Clear();

        var second = await _h.RunAsync();

        Assert.Equal(ScoringRunStatuses.Succeeded, second.Status);
        Assert.Equal(0, second.SimDaysScored);
        Assert.Empty(_h.ScoreCalls);
        Assert.Equal(12, await ScoreCountAsync());
        Assert.Equal(0, await database.ScalarAsync(
            "SELECT count(*) FROM (SELECT 1 FROM risk_scores GROUP BY sim_id, date HAVING count(*) > 1) d"));
    }

    [DbFact]
    public async Task Force_rescores_existing_sim_days_in_place()
    {
        await _h.AddSimAsync("field", Days(0, 15));
        await _h.RunAsync();
        _h.Scorer = (_, _) => new ScoringHarness.FakeScore(0.42, "escalated", [("peak_mb", 1, 0.3)]);

        var forced = await _h.RunAsync(force: true);

        Assert.Equal(2, forced.SimDaysScored);
        Assert.Equal(2, await ScoreCountAsync());
        Assert.Equal(2, await database.ScalarAsync("SELECT count(*) FROM risk_scores WHERE probability = 0.42"));
    }

    [DbFact]
    public async Task Ml_service_down_fails_cleanly_and_keeps_old_scores()
    {
        await _h.AddSimAsync("field", Days(0, 16));
        await _h.RunAsync(to: ScoringHarness.Day(15));
        Assert.Equal(2, await ScoreCountAsync());
        _h.ScoreCalls.Clear();
        _h.VersionDown = true;

        var down = await _h.RunAsync();

        Assert.Equal(ScoringRunStatuses.Failed, down.Status);
        Assert.Equal(0, down.SimDaysScored);
        Assert.NotNull(down.FinishedAt);
        Assert.False(string.IsNullOrWhiteSpace(down.ErrorSummary));
        Assert.Empty(_h.ScoreCalls);
        Assert.Equal(2, await ScoreCountAsync());
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT count(*) FROM audit_logs WHERE action = 'scoring.run_failed' AND actor_email = 'system' AND category = 'system'"));
    }

    [DbFact]
    public async Task A_failed_batch_stops_the_run_as_partial_and_the_next_run_continues()
    {
        await _h.AddSimAsync("field", Days(0, 19));
        _h.FailOn[ScoringHarness.Day(17)] = 500;

        var partial = await _h.RunAsync();

        Assert.Equal(ScoringRunStatuses.Partial, partial.Status);
        Assert.Equal(3, partial.SimDaysScored);
        Assert.Equal(1, partial.Failures);
        Assert.Equal(ScoringHarness.Day(16), partial.LastDate);
        Assert.Contains("2025-01-18", partial.ErrorSummary);
        Assert.Equal(0, await database.ScalarAsync("SELECT count(*) FROM risk_scores WHERE date >= '2025-01-18'"));

        _h.FailOn.Clear();
        var next = await _h.RunAsync();

        Assert.Equal(ScoringRunStatuses.Succeeded, next.Status);
        Assert.Equal(3, next.SimDaysScored);
        Assert.Equal(ScoringHarness.Day(17), next.FirstDate);
        Assert.Equal(6, await ScoreCountAsync());
    }

    [DbFact]
    public async Task An_invalid_ml_response_stores_nothing_for_that_batch()
    {
        await _h.AddSimAsync("field", Days(0, 14));
        _h.Scorer = (_, _) => new ScoringHarness.FakeScore(1.7, "flag", [("peak_mb", 1, 1)]);

        var run = await _h.RunAsync();

        Assert.Equal(ScoringRunStatuses.Failed, run.Status);
        Assert.Contains("invalid", run.ErrorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await ScoreCountAsync());
    }

    [DbFact]
    public async Task Scope_limits_sims_and_dates()
    {
        var a = await _h.AddSimAsync("field", Days(0, 19));
        await _h.AddSimAsync("field", Days(0, 19));

        var run = await _h.RunAsync(sims: [a], from: ScoringHarness.Day(16), to: ScoringHarness.Day(17));

        Assert.Equal(2, run.SimDaysScored);
        Assert.All(_h.ScoreCalls, c => Assert.Equal([a], c.Sims));
        Assert.Equal(2, await ScoreCountAsync());
    }

    [DbFact]
    public async Task Batches_respect_the_configured_size()
    {
        await _h.DisposeAsync();
        _h = await ScoringHarness.StartAsync(database, new() { ["Scoring:BatchSize"] = "2" });
        for (var i = 0; i < 5; i++) await _h.AddSimAsync("field", Days(0, 14));

        var run = await _h.RunAsync();

        Assert.Equal(5, run.SimDaysScored);
        Assert.Equal(1, run.DatesScored);
        Assert.Equal([2, 2, 1], _h.ScoreCalls.Select(c => c.Sims.Length).ToArray());
    }

    [DbFact]
    public async Task Triggers_arriving_while_a_run_is_queued_are_merged()
    {
        var first = await _h.EnqueueAsync(sims: [3], from: ScoringHarness.Day(20), to: ScoringHarness.Day(25),
            trigger: ScoringTriggers.Import);
        var second = await _h.EnqueueAsync(sims: [1, 3], from: ScoringHarness.Day(10), to: ScoringHarness.Day(22),
            trigger: ScoringTriggers.Import);

        Assert.Equal(first, second);
        await using (var db = database.NewContext())
        {
            var run = await db.ScoringRuns.AsNoTracking().SingleAsync();
            Assert.Equal([1, 3], run.ScopeSimIds!);
            Assert.Equal(ScoringHarness.Day(10), run.ScopeFrom);
            Assert.Equal(ScoringHarness.Day(25), run.ScopeTo);
            Assert.Equal(1, run.MergedRequests);
            Assert.Equal(ScoringTriggers.Import, run.Trigger);
        }

        var third = await _h.EnqueueAsync(force: true);

        Assert.Equal(first, third);
        await using (var db = database.NewContext())
        {
            var run = await db.ScoringRuns.AsNoTracking().SingleAsync();
            Assert.Null(run.ScopeSimIds);
            Assert.Null(run.ScopeFrom);
            Assert.Null(run.ScopeTo);
            Assert.True(run.Force);
            Assert.Equal(ScoringTriggers.Manual, run.Trigger);
            Assert.Equal("tester@example.com", run.RequestedBy);
        }
    }

    [DbFact]
    public async Task A_trigger_during_a_running_run_queues_a_new_run()
    {
        var running = await _h.EnqueueAsync();
        await using (var scope = _h.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SimGuard.Api.Data.SimGuardDbContext>();
            Assert.Equal(running, await _h.Services.GetRequiredService<ScoringQueue>().TakeNextAsync(db, CancellationToken.None));
        }

        var queued = await _h.EnqueueAsync(trigger: ScoringTriggers.Schedule);
        var merged = await _h.EnqueueAsync(trigger: ScoringTriggers.Import, sims: [1]);

        Assert.NotEqual(running, queued);
        Assert.Equal(queued, merged);
        Assert.Equal(1, await database.ScalarAsync("SELECT count(*) FROM scoring_runs WHERE status = 'queued'"));
        Assert.Equal(1, await database.ScalarAsync("SELECT count(*) FROM scoring_runs WHERE status = 'running'"));
    }
}
