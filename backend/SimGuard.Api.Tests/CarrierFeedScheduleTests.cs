using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimGuard.Api.Feed;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.Tests;

// The daily carrier feed sync (CarrierFeed:DailySyncTime in CarrierFeed:TimeZone),
// driven by a fake clock and a fake list of feeds. Nairobi is UTC+3, so the
// default 01:30 Nairobi slot is 22:30 UTC the evening before.
public sealed class CarrierFeedScheduleTests
{
    private static DateTimeOffset Nairobi(int month, int day, int hour, int minute, int second = 0) =>
        new(2026, month, day, hour, minute, second, TimeSpan.FromHours(3));

    private static DateTime Utc(DateTimeOffset value) => value.UtcDateTime;

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;

        // The server's own zone; the schedule must not depend on it.
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    }

    private sealed class FixedOptions(CarrierFeedOptions value) : IOptionsMonitor<CarrierFeedOptions>
    {
        public CarrierFeedOptions CurrentValue => value;

        public CarrierFeedOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<CarrierFeedOptions, string?> listener) => null;
    }

    // Stands in for the carriers table and the runner: a run takes 20 seconds
    // and sets the carrier's last sync time, as CarrierFeedSync does.
    private sealed class FakeFeeds(FakeClock clock) : IFeedSyncTarget
    {
        public Dictionary<int, DateTime?> LastSyncAt { get; } = new();

        public List<(int Id, DateTime At)> Runs { get; } = [];

        public Task<IReadOnlyList<int>> ListDueAsync(DateTime slotUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<int>>(LastSyncAt
                .Where(f => FeedSchedule.IsDue(f.Value, slotUtc))
                .Select(f => f.Key)
                .Order()
                .ToList());

        public Task RunAsync(int carrierId, CancellationToken cancellationToken)
        {
            Runs.Add((carrierId, clock.Now.UtcDateTime));
            LastSyncAt[carrierId] = clock.Now.UtcDateTime.AddSeconds(20);
            return Task.CompletedTask;
        }

        // Sync now from the web app.
        public void SyncNow(int carrierId) => LastSyncAt[carrierId] = clock.Now.UtcDateTime.AddSeconds(20);
    }

    private sealed class Rig
    {
        public Rig(DateTimeOffset start, CarrierFeedOptions? options = null)
        {
            Clock = new FakeClock(start);
            Feeds = new FakeFeeds(Clock);
            Options = options ?? new CarrierFeedOptions();
        }

        public FakeClock Clock { get; }

        public FakeFeeds Feeds { get; }

        public CarrierFeedOptions Options { get; }

        public CarrierFeedWorker Worker { get; private set; } = null!;

        // A new API process: a new worker with no memory of handled slots.
        public Rig Start()
        {
            var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            Worker = new CarrierFeedWorker(scopes, Feeds, new FixedOptions(Options), Clock, NullLogger<CarrierFeedWorker>.Instance);
            return this;
        }

        public Task<FeedCheck?> CheckAsync() => Worker.CheckAsync(CancellationToken.None);

        // Checks every 30 seconds (the worker's tick) up to and including the end.
        public async Task<List<FeedCheck>> RunUntilAsync(DateTimeOffset end)
        {
            var checks = new List<FeedCheck>();

            while (Clock.Now <= end)
            {
                if (await CheckAsync() is { } check)
                {
                    checks.Add(check);
                }

                Clock.Now += TimeSpan.FromSeconds(30);
            }

            Clock.Now = end;
            return checks;
        }
    }

    [Fact]
    public void Defaults_are_01_30_in_Africa_Nairobi()
    {
        var options = new CarrierFeedOptions();
        Assert.Equal("01:30", options.DailySyncTime);
        Assert.Equal("Africa/Nairobi", options.TimeZone);

        var schedule = FeedSchedule.From(options);
        Assert.Equal(new DateTime(2026, 11, 13, 22, 30, 0, DateTimeKind.Utc), schedule.SlotUtc(new DateOnly(2026, 11, 14)));
    }

    [Fact]
    public async Task Normal_run_syncs_each_feed_once_at_01_30_before_the_02_00_scoring()
    {
        var rig = new Rig(Nairobi(11, 13, 12, 0));
        rig.Feeds.LastSyncAt[1] = Utc(Nairobi(11, 13, 1, 30, 20));
        rig.Feeds.LastSyncAt[2] = Utc(Nairobi(11, 13, 1, 30, 40));
        rig.Start();

        var first = await rig.CheckAsync();
        Assert.NotNull(first);
        Assert.True(first.AtStart);
        Assert.Empty(first.CarrierIds);

        await rig.RunUntilAsync(Nairobi(11, 14, 1, 29, 30));
        Assert.Empty(rig.Feeds.Runs);

        await rig.RunUntilAsync(Nairobi(11, 14, 2, 0));
        Assert.Equal([1, 2], rig.Feeds.Runs.Select(r => r.Id));
        Assert.All(rig.Feeds.Runs, r =>
        {
            Assert.InRange(r.At, Utc(Nairobi(11, 14, 1, 30)), Utc(Nairobi(11, 14, 1, 30, 30)));
            Assert.True(r.At < Utc(Nairobi(11, 14, 2, 0)));
        });

        // Two more days: exactly one run per feed per day.
        await rig.RunUntilAsync(Nairobi(11, 16, 12, 0));
        Assert.Equal(6, rig.Feeds.Runs.Count);
        Assert.Equal(3, rig.Feeds.Runs.Count(r => r.Id == 1));
    }

    [Fact]
    public async Task Restart_before_the_time_waits_for_the_time()
    {
        var rig = new Rig(Nairobi(11, 14, 1, 0));
        rig.Feeds.LastSyncAt[1] = Utc(Nairobi(11, 13, 1, 30, 10));
        rig.Start();

        Assert.Empty((await rig.CheckAsync())!.CarrierIds);

        await rig.RunUntilAsync(Nairobi(11, 14, 1, 29, 30));
        Assert.Empty(rig.Feeds.Runs);

        await rig.RunUntilAsync(Nairobi(11, 14, 1, 31));
        var run = Assert.Single(rig.Feeds.Runs);
        Assert.Equal(Utc(Nairobi(11, 14, 1, 30)), run.At);
    }

    [Fact]
    public async Task Restart_after_the_time_does_not_sync_again_when_the_slot_already_synced()
    {
        var rig = new Rig(Nairobi(11, 14, 9, 0));
        rig.Feeds.LastSyncAt[1] = Utc(Nairobi(11, 14, 1, 30, 5));
        rig.Feeds.LastSyncAt[2] = Utc(Nairobi(11, 14, 1, 30, 25));
        rig.Start();

        await rig.RunUntilAsync(Nairobi(11, 15, 1, 29, 30));

        Assert.Empty(rig.Feeds.Runs);
    }

    [Fact]
    public async Task Restart_after_a_missed_time_runs_one_catch_up_and_never_twice_for_the_slot()
    {
        var rig = new Rig(Nairobi(11, 14, 9, 0));
        rig.Feeds.LastSyncAt[1] = Utc(Nairobi(11, 13, 1, 30, 5));
        rig.Feeds.LastSyncAt[2] = Utc(Nairobi(11, 13, 1, 30, 25));
        rig.Start();

        var catchUp = await rig.CheckAsync();
        Assert.NotNull(catchUp);
        Assert.True(catchUp.AtStart);
        Assert.Equal(Utc(Nairobi(11, 14, 1, 30)), catchUp.SlotUtc);
        Assert.Equal([1, 2], rig.Feeds.Runs.Select(r => r.Id));

        // The same process, and a second restart ten minutes later: nothing more for that slot.
        await rig.RunUntilAsync(Nairobi(11, 14, 9, 10));
        rig.Start();
        await rig.RunUntilAsync(Nairobi(11, 15, 1, 29, 30));
        Assert.Equal(2, rig.Feeds.Runs.Count);

        // The next night runs as usual.
        await rig.RunUntilAsync(Nairobi(11, 15, 1, 31));
        Assert.Equal(4, rig.Feeds.Runs.Count);
    }

    [Fact]
    public async Task Several_missed_nights_give_one_catch_up_run()
    {
        var rig = new Rig(Nairobi(11, 14, 9, 0));
        rig.Feeds.LastSyncAt[1] = Utc(Nairobi(11, 10, 1, 30, 5));
        rig.Feeds.LastSyncAt[2] = null; // never synced
        rig.Start();

        await rig.RunUntilAsync(Nairobi(11, 15, 1, 0));

        Assert.Equal([1, 2], rig.Feeds.Runs.Select(r => r.Id));
        Assert.All(rig.Feeds.Runs, r => Assert.Equal(Utc(Nairobi(11, 14, 9, 0)), r.At));
    }

    [Fact]
    public async Task Sync_now_after_the_time_counts_for_the_slot_but_before_the_time_does_not()
    {
        var rig = new Rig(Nairobi(11, 13, 12, 0));
        rig.Feeds.LastSyncAt[1] = Utc(Nairobi(11, 13, 1, 30, 5));
        rig.Feeds.LastSyncAt[2] = Utc(Nairobi(11, 13, 1, 30, 25));
        rig.Start();
        await rig.RunUntilAsync(Nairobi(11, 14, 1, 0));

        rig.Feeds.SyncNow(2); // 01:00, before the slot
        await rig.RunUntilAsync(Nairobi(11, 14, 1, 29, 30));
        rig.Clock.Now = Nairobi(11, 14, 1, 30, 1);
        rig.Feeds.SyncNow(1); // 01:30:01, after the slot, before the next check

        await rig.RunUntilAsync(Nairobi(11, 14, 3, 0));

        var run = Assert.Single(rig.Feeds.Runs);
        Assert.Equal(2, run.Id);
    }

    [Fact]
    public async Task Disabled_runs_nothing()
    {
        var rig = new Rig(Nairobi(11, 14, 9, 0), new CarrierFeedOptions { Enabled = false });
        rig.Feeds.LastSyncAt[1] = null;
        rig.Start();

        await rig.RunUntilAsync(Nairobi(11, 15, 2, 0));

        Assert.Empty(rig.Feeds.Runs);
    }

    [Fact]
    public async Task The_time_is_read_in_the_configured_zone_not_the_server_clock()
    {
        // The fake server clock is in Los Angeles; the slot is still 01:30 Nairobi = 22:30 UTC.
        var rig = new Rig(new DateTimeOffset(2026, 11, 13, 22, 0, 0, TimeSpan.Zero));
        rig.Feeds.LastSyncAt[1] = Utc(Nairobi(11, 13, 1, 30, 5));
        rig.Start();

        await rig.RunUntilAsync(new DateTimeOffset(2026, 11, 13, 22, 29, 30, TimeSpan.Zero));
        Assert.Empty(rig.Feeds.Runs);

        await rig.RunUntilAsync(new DateTimeOffset(2026, 11, 13, 22, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateTime(2026, 11, 13, 22, 30, 0, DateTimeKind.Utc), Assert.Single(rig.Feeds.Runs).At);
    }

    [Fact]
    public async Task Another_zone_moves_the_slot()
    {
        var rig = new Rig(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero),
            new CarrierFeedOptions { DailySyncTime = "01:30", TimeZone = "UTC" });
        rig.Feeds.LastSyncAt[1] = new DateTime(2026, 11, 13, 1, 30, 5, DateTimeKind.Utc);
        rig.Start();

        await rig.RunUntilAsync(new DateTimeOffset(2026, 11, 14, 2, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTime(2026, 11, 14, 1, 30, 0, DateTimeKind.Utc), Assert.Single(rig.Feeds.Runs).At);
    }

    [Theory]
    [InlineData("2026-07-01", "2026-07-01T00:30:00Z")] // summer time, UTC+1
    [InlineData("2026-12-01", "2026-12-01T01:30:00Z")] // winter, UTC+0
    [InlineData("2026-03-29", "2026-03-29T01:00:00Z")] // 01:30 does not exist; runs at 02:00 local
    public void Daylight_saving_zones_follow_the_local_clock(string day, string expectedUtc)
    {
        var schedule = FeedSchedule.From(new CarrierFeedOptions { DailySyncTime = "01:30", TimeZone = "Europe/London" });

        var slot = schedule.SlotUtc(DateOnly.Parse(day));

        Assert.Equal(DateTime.Parse(expectedUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal), slot);
    }

    [Theory]
    [InlineData("25:00", "Africa/Nairobi", "DailySyncTime")]
    [InlineData("1.30", "Africa/Nairobi", "DailySyncTime")]
    [InlineData("01:30", "Mars/Olympus", "TimeZone")]
    [InlineData("01:30", "", "TimeZone")]
    public void Wrong_settings_are_refused_with_the_setting_name(string time, string zone, string named)
    {
        Assert.False(FeedSchedule.TryCreate(time, zone, out _, out var error));
        Assert.Contains(named, error);
    }

    [Fact]
    public void Latest_and_next_slot_around_the_time()
    {
        var schedule = FeedSchedule.From(new CarrierFeedOptions());

        Assert.Equal(Utc(Nairobi(11, 13, 1, 30)), schedule.LatestSlotUtc(Utc(Nairobi(11, 14, 1, 29, 59))));
        Assert.Equal(Utc(Nairobi(11, 14, 1, 30)), schedule.LatestSlotUtc(Utc(Nairobi(11, 14, 1, 30))));
        Assert.Equal(Utc(Nairobi(11, 14, 1, 30)), schedule.NextSlotUtc(Utc(Nairobi(11, 14, 1, 29, 59))));
        Assert.Equal(Utc(Nairobi(11, 15, 1, 30)), schedule.NextSlotUtc(Utc(Nairobi(11, 14, 1, 30))));
    }
}

// The database side: which feeds are due, and that the 02:00 scheduled scoring
// run scores a day the sync has just added, once.
public sealed class CarrierFeedScheduleDbTests(TestDatabase database) : IClassFixture<TestDatabase>, IAsyncLifetime
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

    [DbFact]
    public async Task Only_enabled_configured_feeds_not_synced_since_the_slot_are_due()
    {
        var slot = new DateTime(2026, 11, 13, 22, 30, 0, DateTimeKind.Utc);
        await database.ExecuteAsync($"""
            UPDATE carriers SET feed_enabled = false;
            INSERT INTO carriers (name, feed_base_url, feed_enabled, feed_key_set, last_sync_at) VALUES
              ('Due never synced', 'https://a.example', true, true, NULL),
              ('Due synced before', 'https://b.example', true, true, '2026-11-13 22:29:59+00'),
              ('Synced at the slot', 'https://c.example', true, true, '2026-11-13 22:30:00+00'),
              ('Synced after', 'https://d.example', true, true, '2026-11-14 06:00:00+00'),
              ('Disabled', 'https://e.example', false, true, NULL),
              ('No key', 'https://f.example', true, false, NULL),
              ('No address', NULL, true, true, NULL);
            """);
        var expected = new[]
        {
            (int)await database.ScalarAsync("SELECT id FROM carriers WHERE name = 'Due never synced'"),
            (int)await database.ScalarAsync("SELECT id FROM carriers WHERE name = 'Due synced before'")
        };

        var target = new DbFeedSyncTarget(
            _h.Services.GetRequiredService<IServiceScopeFactory>(),
            new CarrierFeedRunner(_h.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<CarrierFeedRunner>.Instance));

        var due = await target.ListDueAsync(slot, CancellationToken.None);

        Assert.Equal(expected.Order(), due);
    }

    [DbFact]
    public async Task The_scheduled_scoring_run_scores_a_newly_synced_day_once()
    {
        var sim = await _h.AddSimAsync("field", Enumerable.Range(0, 20)); // days 0..19, scorable 14..19
        var first = await _h.RunAsync(trigger: ScoringTriggers.Schedule);
        Assert.Equal(6, first.SimDaysScored);

        // The feed sync adds day 20 (what CarrierFeedSync saves for a new day).
        await database.ExecuteAsync($"""
            INSERT INTO daily_usage (sim_id, date, total_download_mb, total_upload_mb, data_session_count,
                data_duration_s, hotspot_sessions, public_apn_sessions, unique_towers, geo_flag_count, peak_mb,
                offpeak_mb, total_mb, roaming_sessions, billed_mb, tier, voice_minutes_used, sms_count,
                voice_minutes_remaining, sms_remaining)
            SELECT sim_id, date + 1, total_download_mb, total_upload_mb, data_session_count,
                data_duration_s, hotspot_sessions, public_apn_sessions, unique_towers, geo_flag_count, peak_mb,
                offpeak_mb, total_mb, roaming_sessions, billed_mb, tier, voice_minutes_used, sms_count,
                voice_minutes_remaining, sms_remaining
            FROM daily_usage WHERE sim_id = {sim} AND date = '{ScoringHarness.Day(19):yyyy-MM-dd}'
            """);
        _h.ScoreCalls.Clear();

        var scheduled = await _h.RunAsync(trigger: ScoringTriggers.Schedule);

        Assert.Equal(1, scheduled.SimDaysScored);
        var call = Assert.Single(_h.ScoreCalls);
        Assert.Equal(ScoringHarness.Day(20), call.Date);
        Assert.Equal([sim], call.Sims);

        // A second scheduled run (or the import-triggered one already ran) scores nothing again.
        _h.ScoreCalls.Clear();
        var again = await _h.RunAsync(trigger: ScoringTriggers.Schedule);
        Assert.Equal(0, again.SimDaysScored);
        Assert.Empty(_h.ScoreCalls);
        Assert.Equal(1, await database.ScalarAsync(
            $"SELECT count(*) FROM risk_scores WHERE sim_id = {sim} AND date = '{ScoringHarness.Day(20):yyyy-MM-dd}'"));
    }

    [DbFact]
    public async Task The_same_usage_day_cannot_be_stored_twice()
    {
        var sim = await _h.AddSimAsync("field", [0]);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteAsync($"""
            INSERT INTO daily_usage (sim_id, date, total_download_mb, total_upload_mb, data_session_count,
                data_duration_s, hotspot_sessions, public_apn_sessions, unique_towers, geo_flag_count, peak_mb,
                offpeak_mb, total_mb, roaming_sessions, billed_mb, tier, voice_minutes_used, sms_count,
                voice_minutes_remaining, sms_remaining)
            SELECT sim_id, date, total_download_mb, total_upload_mb, data_session_count,
                data_duration_s, hotspot_sessions, public_apn_sessions, unique_towers, geo_flag_count, peak_mb,
                offpeak_mb, total_mb, roaming_sessions, billed_mb, tier, voice_minutes_used, sms_count,
                voice_minutes_remaining, sms_remaining
            FROM daily_usage WHERE sim_id = {sim}
            """));

        Assert.Contains("idx_daily_usage_sim_date", ex.Message);
        Assert.Equal(1, await database.ScalarAsync($"SELECT count(*) FROM daily_usage WHERE sim_id = {sim}"));
    }
}
