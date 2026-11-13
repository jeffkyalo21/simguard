using Microsoft.Extensions.Configuration;
using SimGuard.CarrierMock.Release;

namespace SimGuard.CarrierMock.Tests;

public sealed class ReleaseStateTests : IDisposable
{
    // 2025-06-01 21:00 UTC is 2025-06-02 00:00 in Nairobi (UTC+3, no daylight saving).
    private static readonly DateTimeOffset NairobiMidnight = new(2025, 6, 1, 21, 0, 0, TimeSpan.Zero);

    private readonly TestData _data = new(10);
    private readonly ListLogger _log = new();
    private readonly FakeClock _clock = new(NairobiMidnight);

    public void Dispose() => _data.Dispose();

    private IReadOnlyList<DateOnly> Dates => Enumerable.Range(0, _data.Days).Select(d => TestData.FirstDate.AddDays(d)).ToList();

    private ReleaseSettings Settings(Dictionary<string, string?>? values = null)
    {
        var all = new Dictionary<string, string?>
        {
            ["MOCK_START_DAY"] = "3",
            ["MOCK_STATE_FILE"] = _data.StateFile
        };

        foreach (var (k, v) in values ?? [])
        {
            all[k] = v;
        }

        return ReleaseSettings.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(all).Build(), Dates);
    }

    private ReleaseState NewState(ReleaseSettings? settings = null) => new(settings ?? Settings(), Dates, _clock, _log);

    [Fact]
    public void First_start_releases_through_the_start_day_and_nothing_more()
    {
        var state = NewState();

        Assert.Equal(3, state.ReleasedThroughDay);
        Assert.Null(state.Tick());
        Assert.True(state.IsAvailable(new DateOnly(2025, 1, 3)));
        Assert.False(state.IsAvailable(new DateOnly(2025, 1, 4)));
        Assert.True(File.Exists(_data.StateFile));
    }

    [Fact]
    public void Default_start_day_is_80_or_the_last_day_of_shorter_data()
    {
        var empty = new ConfigurationBuilder().Build();
        var ninety = Enumerable.Range(0, 90).Select(d => TestData.FirstDate.AddDays(d)).ToList();

        Assert.Equal(80, ReleaseSettings.FromConfiguration(empty, ninety).StartDay);
        Assert.Equal(10, ReleaseSettings.FromConfiguration(empty, Dates).StartDay);
    }

    [Fact]
    public void Pointer_survives_a_restart()
    {
        NewState().ReleaseNext("test");

        var restarted = NewState();

        Assert.Equal(4, restarted.ReleasedThroughDay);
    }

    [Fact]
    public void Nightly_release_happens_at_the_release_time_in_the_configured_zone()
    {
        var state = NewState();

        _clock.Now = NairobiMidnight.AddMinutes(59);           // 00:59 Nairobi
        Assert.Null(state.Tick());

        _clock.Now = NairobiMidnight.AddHours(1);              // 01:00 Nairobi, 22:00 UTC the day before
        var outcome = state.Tick();
        Assert.NotNull(outcome);
        Assert.Equal(ReleaseStatuses.Released, outcome.Status);
        Assert.Equal(4, state.ReleasedThroughDay);

        _clock.Now = NairobiMidnight.AddHours(5);              // the same night again
        Assert.Null(state.Tick());
        Assert.Equal(4, state.ReleasedThroughDay);

        _clock.Now = NairobiMidnight.AddDays(1).AddHours(1);   // next night
        Assert.NotNull(state.Tick());
        Assert.Equal(5, state.ReleasedThroughDay);
    }

    [Fact]
    public void Release_time_and_zone_are_configurable()
    {
        var state = NewState(Settings(new() { ["MOCK_RELEASE_TIME"] = "06:30", ["MOCK_TIMEZONE"] = "UTC" }));

        _clock.Now = new DateTimeOffset(2025, 6, 2, 6, 29, 0, TimeSpan.Zero);
        Assert.Null(state.Tick());

        _clock.Now = new DateTimeOffset(2025, 6, 2, 6, 30, 0, TimeSpan.Zero);
        Assert.NotNull(state.Tick());
    }

    [Fact]
    public void Missed_nights_release_one_day_at_the_next_start_never_more()
    {
        NewState();

        // Down through the release nights of 2, 3, 4 and 5 June; back at noon on 5 June (Nairobi).
        _clock.Now = NairobiMidnight.AddDays(3).AddHours(12);
        var restarted = NewState();
        var outcome = restarted.Tick();

        Assert.NotNull(outcome);
        Assert.Equal(3, outcome.SkippedNights);
        Assert.Equal(4, restarted.ReleasedThroughDay);
        Assert.Null(restarted.Tick());
        Assert.Equal(1, _log.Count("nightly releases were missed"));

        // Then one per night again.
        _clock.Now = NairobiMidnight.AddDays(4).AddHours(1);
        Assert.NotNull(restarted.Tick());
        Assert.Equal(5, restarted.ReleasedThroughDay);
    }

    [Fact]
    public void Restart_after_the_nights_release_does_not_release_again()
    {
        var state = NewState();
        _clock.Now = NairobiMidnight.AddHours(1);
        state.Tick();
        Assert.Equal(4, state.ReleasedThroughDay);

        _clock.Now = NairobiMidnight.AddHours(2);
        var restarted = NewState();

        Assert.Null(restarted.Tick());
        Assert.Equal(4, restarted.ReleasedThroughDay);
    }

    [Fact]
    public void Manual_release_comes_on_top_of_the_nightly_one()
    {
        var state = NewState();

        state.ReleaseNext("manual");
        Assert.Equal(4, state.ReleasedThroughDay);

        _clock.Now = NairobiMidnight.AddHours(1);
        Assert.NotNull(state.Tick());
        Assert.Equal(5, state.ReleasedThroughDay);
    }

    [Fact]
    public void End_of_data_keeps_the_last_day_says_no_more_days_and_logs_once()
    {
        var state = NewState(Settings(new() { ["MOCK_START_DAY"] = "9" }));

        Assert.Equal(ReleaseStatuses.Released, state.ReleaseNext("manual").Status);
        var end = state.ReleaseNext("manual");
        var again = state.ReleaseNext("manual");

        _clock.Now = NairobiMidnight.AddHours(1);
        var nightly = state.Tick();

        Assert.Equal(ReleaseStatuses.NoMoreDays, end.Status);
        Assert.Equal(10, end.ReleasedThroughDay);
        Assert.Equal(new DateOnly(2025, 1, 10), end.ReleasedThroughDate);
        Assert.Equal(0, end.DaysLeft);
        Assert.Equal(ReleaseStatuses.NoMoreDays, again.Status);
        Assert.Equal(ReleaseStatuses.NoMoreDays, nightly!.Status);
        Assert.Equal(10, state.ReleasedThroughDay);
        Assert.False(state.IsAvailable(new DateOnly(2025, 1, 11)));
        Assert.Equal(1, _log.Count("No more days"));

        // Also not again after a restart.
        NewState(Settings(new() { ["MOCK_START_DAY"] = "9" })).ReleaseNext("manual");
        Assert.Equal(1, _log.Count("No more days"));
    }

    [Fact]
    public void Reset_goes_back_to_the_start_day_or_a_given_day()
    {
        var state = NewState();
        state.ReleaseNext("manual");
        state.ReleaseNext("manual");

        Assert.Equal(3, state.Reset(null).ReleasedThroughDay);
        Assert.Equal(7, state.Reset(7).ReleasedThroughDay);
        Assert.Equal(10, state.Reset(99).ReleasedThroughDay);
        Assert.Equal(10, NewState().ReleasedThroughDay);
    }

    [Fact]
    public void Date_shift_moves_served_dates_by_whole_weeks()
    {
        var state = NewState(Settings(new() { ["MOCK_DATE_SHIFT_DAYS"] = "7" }));

        Assert.Equal(new DateOnly(2025, 1, 1), state.ToDataDate(new DateOnly(2025, 1, 8)));
        Assert.True(state.IsAvailable(new DateOnly(2025, 1, 10)));   // data day 2025-01-03 = day 3
        Assert.False(state.IsAvailable(new DateOnly(2025, 1, 11)));
        Assert.Equal(state.ToServedDate(TestData.FirstDate).DayOfWeek, TestData.FirstDate.DayOfWeek);
    }

    [Theory]
    [InlineData("MOCK_DATE_SHIFT_DAYS", "10")]
    [InlineData("MOCK_DATE_SHIFT_DAYS", "week")]
    [InlineData("MOCK_RELEASE_TIME", "1am")]
    [InlineData("MOCK_START_DAY", "-1")]
    [InlineData("MOCK_TIMEZONE", "Mars/Olympus")]
    [InlineData("MOCK_SCHEDULER_ENABLED", "maybe")]
    public void Bad_settings_stop_the_start_with_the_variable_name(string key, string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Settings(new() { [key] = value }));
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void Older_start_date_setting_still_works()
    {
        var last = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CarrierMock:StartDate"] = "last" }).Build();
        var date = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CarrierMock:StartDate"] = "2025-01-04" }).Build();

        Assert.Equal(10, ReleaseSettings.FromConfiguration(last, Dates).StartDay);
        Assert.Equal(4, ReleaseSettings.FromConfiguration(date, Dates).StartDay);
    }

    [Fact]
    public void A_damaged_state_file_stops_the_start_instead_of_replaying_days()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_data.StateFile)!);
        File.WriteAllText(_data.StateFile, "{ not json");

        var ex = Assert.Throws<InvalidOperationException>(() => NewState());
        Assert.Contains("release state file", ex.Message);
    }

    [Fact]
    public void Status_reports_pointer_next_release_and_days_left()
    {
        var state = NewState();
        var json = System.Text.Json.JsonSerializer.SerializeToElement(state.Status());

        Assert.Equal(3, json.GetProperty("releasedThroughDay").GetInt32());
        Assert.Equal(7, json.GetProperty("daysLeft").GetInt32());
        Assert.Equal("2025-06-02T01:00:00+03:00", json.GetProperty("nextReleaseAt").GetString());
        Assert.Equal("2025-06-01T22:00:00Z", json.GetProperty("nextReleaseAtUtc").GetString());
        Assert.Equal("releasing", json.GetProperty("status").GetString());
    }
}
