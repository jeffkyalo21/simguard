using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimGuard.Api.Data;
using SimGuard.Api.Scoring;
using SimGuard.Api.TierRecommendations;

namespace SimGuard.Api.Tests;

public sealed class TierRecommendationTests(TestDatabase database) : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private ScoringHarness _h = null!;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.Available) return;
        _h = await ScoringHarness.StartAsync(database);
        await _h.ResetAsync();
        await database.ExecuteAsync("UPDATE tier_recommendation_settings SET interval_days = 90");
    }

    public async Task DisposeAsync()
    {
        if (_h is not null) await _h.DisposeAsync();
    }

    private static IEnumerable<int> Days(int from, int to) => Enumerable.Range(from, to - from + 1);

    private TierRecommendationService Service => _h.Services.GetRequiredService<TierRecommendationService>();

    // Starts a manual run and waits for it to finish.
    private async Task<TierRunStart> RunAsync(bool skipInterval = false)
    {
        var start = await Service.TryStartAsync(TierRunTriggers.Manual, null, "officer@example.com", skipInterval, CancellationToken.None);
        await Service.Current;
        return start;
    }

    private async Task<Models.TierRecommendationRun> LastRunAsync()
    {
        await using var db = database.NewContext();
        return await db.TierRecommendationRuns.AsNoTracking().OrderByDescending(r => r.Id).FirstAsync();
    }

    private async Task<TierSchedule> ScheduleAsync(DateTime now)
    {
        await using var db = database.NewContext();
        return await Service.GetScheduleAsync(db, now, CancellationToken.None);
    }

    [DbFact]
    public async Task A_run_stores_only_upgrades_and_downgrades_for_the_latest_cycle()
    {
        var up = await _h.AddSimAsync("field", Days(0, 75));
        var down = await _h.AddSimAsync("field", Days(0, 75));
        var same = await _h.AddSimAsync("field", Days(0, 75));
        var old = await _h.AddSimAsync("field", Days(0, 40));   // no usage in cycle 2
        _h.Recommender = sim => sim == up ? ("upgrade", "manager") : sim == down ? ("downgrade", "support") : ("no_change", null);

        var start = await RunAsync();

        Assert.NotNull(start.RunId);
        var run = await LastRunAsync();
        Assert.Equal(TierRunStatuses.Succeeded, run.Status);
        Assert.Equal(2, run.BillingCycle);
        Assert.Equal(3, run.SimsEvaluated);
        Assert.Equal(2, run.RecommendationsCreated);
        Assert.All(_h.RecommendCalls, c => Assert.Equal(2, c.Cycle));
        Assert.DoesNotContain(_h.RecommendCalls.SelectMany(c => c.Sims), s => s == old);

        await using var db = database.NewContext();
        var recs = await db.TierRecommendations.AsNoTracking().OrderBy(r => r.SimId).ToListAsync();
        Assert.Equal([up, down], recs.Select(r => r.SimId).ToArray());
        Assert.Equal("manager", recs[0].RecommendedTier);
        Assert.Equal("upgrade", recs[0].Recommendation);
        Assert.Equal("open", recs[0].Status);
        Assert.Contains("over the field bundle", recs[0].Reason);
        Assert.Contains("unused", recs[1].Reason);
        Assert.Equal("field", (await db.Sims.AsNoTracking().SingleAsync(s => s.Id == up)).Tier);   // nothing applied
        Assert.DoesNotContain(recs, r => r.SimId == same);
    }

    [DbFact]
    public async Task A_second_run_inside_the_interval_is_refused_with_the_next_allowed_date()
    {
        await _h.AddSimAsync("field", Days(0, 20));
        await RunAsync();
        var first = await LastRunAsync();

        var refused = await RunAsync();

        Assert.Null(refused.RunId);
        Assert.Contains(first.StartedAt.AddDays(90).ToString("yyyy-MM-dd"), refused.Conflict);
        Assert.Equal(first.StartedAt.AddDays(90), refused.NextAllowedAt);

        var forced = await RunAsync(skipInterval: true);
        Assert.NotNull(forced.RunId);
    }

    [DbFact]
    public async Task A_failed_run_does_not_start_the_interval()
    {
        await _h.AddSimAsync("field", Days(0, 20));
        _h.RecommendDown = true;
        await RunAsync();
        Assert.Equal(TierRunStatuses.Failed, (await LastRunAsync()).Status);

        _h.RecommendDown = false;
        var next = await RunAsync();

        Assert.NotNull(next.RunId);
        Assert.Equal(TierRunStatuses.Succeeded, (await LastRunAsync()).Status);
    }

    [DbFact]
    public async Task Open_and_recently_dismissed_recommendations_are_not_recreated()
    {
        var a = await _h.AddSimAsync("field", Days(0, 20));
        var b = await _h.AddSimAsync("field", Days(0, 20));
        _h.Recommender = _ => ("upgrade", "manager");
        await RunAsync();

        // a stays open: not even sent again. b is dismissed: same tier is skipped.
        await database.ExecuteAsync($"UPDATE tier_recommendations SET status = 'dismissed', dismissed_at = now() WHERE sim_id = {b}");
        _h.RecommendCalls.Clear();
        await RunAsync(skipInterval: true);

        Assert.Equal([b], _h.RecommendCalls.SelectMany(c => c.Sims).ToArray());
        Assert.Equal(0, (await LastRunAsync()).RecommendationsCreated);
        Assert.Equal(2, await database.ScalarAsync("SELECT count(*) FROM tier_recommendations"));

        // A different tier for b is a new recommendation.
        _h.Recommender = _ => ("upgrade", "professional");
        await RunAsync(skipInterval: true);
        Assert.Equal(1, (await LastRunAsync()).RecommendationsCreated);

        // Dismissed more than 90 days ago: the same tier may come back.
        await database.ExecuteAsync("DELETE FROM tier_recommendations WHERE status = 'open' AND recommended_tier = 'professional'");
        await database.ExecuteAsync("UPDATE tier_recommendations SET dismissed_at = now() - interval '91 days' WHERE status = 'dismissed'");
        _h.Recommender = _ => ("upgrade", "manager");
        await RunAsync(skipInterval: true);
        Assert.Equal(1, (await LastRunAsync()).RecommendationsCreated);
        Assert.Equal(1, await database.ScalarAsync("SELECT count(*) FROM tier_recommendations WHERE status = 'open' AND sim_id = " + a));
    }

    [DbFact]
    public async Task An_automatic_run_is_due_only_after_interval_plus_grace()
    {
        var now = DateTime.UtcNow;
        Assert.False((await ScheduleAsync(now)).AutomaticRunDue);   // never run: not due

        await _h.AddSimAsync("field", Days(0, 20));
        await RunAsync();
        await database.ExecuteAsync("UPDATE tier_recommendation_runs SET started_at = now() - interval '103 days'");
        Assert.False((await ScheduleAsync(now)).AutomaticRunDue);

        await database.ExecuteAsync("UPDATE tier_recommendation_runs SET started_at = now() - interval '105 days'");
        var schedule = await ScheduleAsync(now);
        Assert.True(schedule.AutomaticRunDue);
        Assert.Equal(90, schedule.IntervalDays);
        Assert.Equal(14, schedule.AutoRunGraceDays);
    }

    [DbFact]
    public async Task A_run_waits_for_a_scoring_run_to_finish()
    {
        await _h.AddSimAsync("field", Days(0, 20));
        var pipelineLock = _h.Services.GetRequiredService<PipelineLock>();
        await pipelineLock.WaitAsync(CancellationToken.None);

        var start = await Service.TryStartAsync(TierRunTriggers.Manual, null, "officer@example.com", false, CancellationToken.None);
        await Task.Delay(500);
        Assert.Empty(_h.RecommendCalls);
        Assert.Equal(TierRunStatuses.Running, (await LastRunAsync()).Status);

        var second = await Service.TryStartAsync(TierRunTriggers.Manual, null, "officer@example.com", true, CancellationToken.None);
        Assert.Null(second.RunId);
        Assert.Contains("already running", second.Conflict);

        pipelineLock.Release();
        await Service.Current;
        Assert.NotNull(start.RunId);
        Assert.NotEmpty(_h.RecommendCalls);
        Assert.Equal(TierRunStatuses.Succeeded, (await LastRunAsync()).Status);
    }
}
