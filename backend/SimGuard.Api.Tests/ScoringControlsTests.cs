using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimGuard.Api.Imports;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.Tests;

public sealed class NairobiClockTests
{
    [Fact]
    public void Next_run_is_today_in_nairobi_when_the_time_is_still_ahead()
    {
        // 2026-11-04 20:00 UTC is 23:00 in Nairobi; 02:00 Nairobi is 23:00 UTC.
        var next = NairobiClock.NextRunUtc(new DateTime(2026, 11, 4, 20, 0, 0, DateTimeKind.Utc), new TimeOnly(2, 0));
        Assert.Equal(new DateTime(2026, 11, 4, 23, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Next_run_moves_to_tomorrow_once_the_time_has_passed()
    {
        var next = NairobiClock.NextRunUtc(new DateTime(2026, 11, 4, 23, 0, 0, DateTimeKind.Utc), new TimeOnly(2, 0));
        Assert.Equal(new DateTime(2026, 11, 5, 23, 0, 0, DateTimeKind.Utc), next);
    }

    [Theory]
    [InlineData("02:00", 2, 0)]
    [InlineData("7:30", 7, 30)]
    [InlineData("bad", 2, 0)]
    [InlineData(null, 2, 0)]
    public void Run_time_parses_with_a_02_00_fallback(string? value, int hour, int minute) =>
        Assert.Equal(new TimeOnly(hour, minute), NairobiClock.ParseRunTime(value));
}

public sealed class ScoringImportTriggerTests(TestDatabase database) : IClassFixture<TestDatabase>, IAsyncLifetime
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

    private static ImportCoverage Coverage(params int[] sims) => new(
        sims.Length, ScoringHarness.Day(0), ScoringHarness.Day(29), 15, sims.Length, 0, 0,
        sims.Select(s => new SimCoverage(s, "x", ScoringHarness.Day(0), ScoringHarness.Day(29), 30, 0, 0, 30, true)).ToList());

    [DbFact]
    public async Task A_usage_import_queues_an_import_run_for_its_sims_and_affected_dates()
    {
        var trigger = _h.Services.GetRequiredService<ScoringImportTrigger>();

        var id = await trigger.AfterUsageImportAsync(Coverage(5, 2), CancellationToken.None);

        Assert.NotNull(id);
        await using var db = database.NewContext();
        var run = await db.ScoringRuns.AsNoTracking().SingleAsync();
        Assert.Equal(ScoringTriggers.Import, run.Trigger);
        Assert.Equal("system", run.RequestedBy);
        Assert.Equal([2, 5], run.ScopeSimIds!);
        Assert.Equal(ScoringHarness.Day(0), run.ScopeFrom);
        Assert.Equal(ScoringHarness.Day(29 + 14), run.ScopeTo);
        Assert.False(run.Force);
    }

    [DbFact]
    public async Task Scoring_disabled_queues_nothing_after_an_import()
    {
        await _h.DisposeAsync();
        _h = await ScoringHarness.StartAsync(database, new() { ["Scoring:Enabled"] = "false" });

        var id = await _h.Services.GetRequiredService<ScoringImportTrigger>()
            .AfterUsageImportAsync(Coverage(1), CancellationToken.None);

        Assert.Null(id);
        Assert.Equal(0, await database.ScalarAsync("SELECT count(*) FROM scoring_runs"));
    }
}
