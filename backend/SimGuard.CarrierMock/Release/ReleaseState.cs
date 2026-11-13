using System.Globalization;
using System.Text.Json;

namespace SimGuard.CarrierMock.Release;

public static class ReleaseStatuses
{
    public const string Released = "released";
    public const string NoMoreDays = "no more days";
    public const string Releasing = "releasing";
}

public sealed record ReleaseOutcome(string Status, int ReleasedThroughDay, DateOnly? ReleasedThroughDate, int DaysLeft, int SkippedNights);

// The pointer released_through_day: the feed serves data days 1 to N only.
// Day numbers count the distinct dates in usage.csv, oldest first (day 1 is
// the first date). The pointer and the last nightly slot are kept in
// MOCK_STATE_FILE (when set), written atomically, so a restart neither
// rewinds nor repeats a night.
//
// Nightly rule: at most one day per night. A start after downtime releases
// one day for the missed nights together (the rest of them are skipped and
// logged), then one per night again. Manual releases come on top and do not
// move the nightly schedule.
public sealed class ReleaseState
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly ReleaseSettings _settings;
    private readonly IReadOnlyList<DateOnly> _dates;
    private readonly ReleaseClock _clock;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    private int _released;
    private DateOnly _lastSlot;
    private bool _endLogged;

    public ReleaseState(ReleaseSettings settings, IReadOnlyList<DateOnly> dates, TimeProvider time, ILogger logger)
    {
        _settings = settings;
        _dates = dates;
        _clock = new ReleaseClock(settings.ReleaseTime, settings.Zone);
        _time = time;
        _logger = logger;

        if (!Load())
        {
            // First start: the start day is released, and the night that has
            // already begun counts as done (no release at the very first start).
            _released = Math.Clamp(settings.StartDay, 0, dates.Count);
            _lastSlot = _clock.LatestSlot(time.GetUtcNow());
            Save();
            _logger.LogInformation("Release pointer created: days 1 to {Day} of {Total} released.", _released, dates.Count);
        }
    }

    public int TotalDays => _dates.Count;

    public int DateShiftDays => _settings.DateShiftDays;

    public int ReleasedThroughDay
    {
        get { lock (_lock) { return _released; } }
    }

    // The newest data date (unshifted) the feed may serve, or null when none is released.
    public DateOnly? ReleasedThroughDataDate
    {
        get { lock (_lock) { return DataDateOf(_released); } }
    }

    // The data date behind a date a client asks for.
    public DateOnly ToDataDate(DateOnly requested) => requested.AddDays(-_settings.DateShiftDays);

    public DateOnly ToServedDate(DateOnly dataDate) => dataDate.AddDays(_settings.DateShiftDays);

    public bool IsAvailable(DateOnly requested)
    {
        var last = ReleasedThroughDataDate;
        return last is not null && ToDataDate(requested) <= last.Value;
    }

    public bool AtEnd => ReleasedThroughDay >= TotalDays;

    // Called by the scheduler (and once at start). Releases at most one day
    // when one or more nightly slots have started since the last one handled.
    public ReleaseOutcome? Tick()
    {
        lock (_lock)
        {
            var latest = _clock.LatestSlot(_time.GetUtcNow());

            if (latest <= _lastSlot)
            {
                return null;
            }

            var nights = latest.DayNumber - _lastSlot.DayNumber;
            _lastSlot = latest;
            var outcome = ReleaseLocked("nightly") with { SkippedNights = nights - 1 };

            if (nights > 1)
            {
                _logger.LogWarning(
                    "{Nights} nightly releases were missed while the mock was down; released one day now and skipped {Skipped}. Use POST /admin/release-next-day to catch up by hand.",
                    nights, nights - 1);
            }

            return outcome;
        }
    }

    public ReleaseOutcome ReleaseNext(string reason)
    {
        lock (_lock)
        {
            return ReleaseLocked(reason);
        }
    }

    // Sets the pointer (default: the start day). The nightly schedule goes on
    // from the next slot; the current night is not released again.
    public ReleaseOutcome Reset(int? day)
    {
        lock (_lock)
        {
            _released = Math.Clamp(day ?? _settings.StartDay, 0, _dates.Count);
            _lastSlot = Max(_lastSlot, _clock.LatestSlot(_time.GetUtcNow()));
            _endLogged = false;
            Save();
            _logger.LogInformation("Release pointer reset: days 1 to {Day} of {Total} released.", _released, _dates.Count);
            return OutcomeLocked(_released >= _dates.Count ? ReleaseStatuses.NoMoreDays : ReleaseStatuses.Released, 0);
        }
    }

    // Pointer to the last data day on or before the (served, so shifted) date.
    public ReleaseOutcome SetThroughDate(DateOnly requested)
    {
        var dataDate = ToDataDate(requested);
        return Reset(_dates.Count(d => d <= dataDate));
    }

    public object Status()
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            var next = _clock.NextRelease(now);
            var last = DataDateOf(_released);

            return new
            {
                status = _released >= _dates.Count ? ReleaseStatuses.NoMoreDays : ReleaseStatuses.Releasing,
                releasedThroughDay = _released,
                releasedThroughDate = last is null ? null : Iso(ToServedDate(last.Value)),
                releasedThroughDataDate = last is null ? null : Iso(last.Value),
                totalDays = _dates.Count,
                daysLeft = _dates.Count - _released,
                nextReleaseAt = _settings.SchedulerEnabled ? TimeZoneInfo.ConvertTime(next, _settings.Zone).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture) : null,
                nextReleaseAtUtc = _settings.SchedulerEnabled ? next.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null,
                lastNightlySlot = Iso(_lastSlot),
                schedulerEnabled = _settings.SchedulerEnabled,
                releaseTime = _settings.ReleaseTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                timeZone = _settings.ZoneId,
                dateShiftDays = _settings.DateShiftDays,
                // A shift (multiple of 7) that would make the newest released day
                // yesterday or the week before, in the configured zone.
                suggestedDateShiftDays = last is null ? (int?)null : SuggestedShift(last.Value, now)
            };
        }
    }

    private int SuggestedShift(DateOnly lastDataDate, DateTimeOffset now)
    {
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _settings.Zone).DateTime).AddDays(-1);
        var gap = yesterday.DayNumber - lastDataDate.DayNumber;
        return (int)Math.Floor(gap / 7.0) * 7;
    }

    private ReleaseOutcome ReleaseLocked(string reason)
    {
        if (_released >= _dates.Count)
        {
            if (!_endLogged)
            {
                _endLogged = true;
                Save();
                _logger.LogWarning("No more days: all {Total} days are released (last {Date}). The pointer stays; nothing is invented or repeated.",
                    _dates.Count, Iso(_dates.Count == 0 ? null : _dates[^1]));
            }

            return OutcomeLocked(ReleaseStatuses.NoMoreDays, 0);
        }

        _released++;
        Save();
        _logger.LogInformation("Released day {Day} of {Total} ({Date}) ({Reason}).",
            _released, _dates.Count, Iso(ToServedDate(_dates[_released - 1])), reason);
        return OutcomeLocked(ReleaseStatuses.Released, 0);
    }

    private ReleaseOutcome OutcomeLocked(string status, int skipped)
    {
        var last = DataDateOf(_released);
        return new ReleaseOutcome(status, _released, last is null ? null : ToServedDate(last.Value), _dates.Count - _released, skipped);
    }

    private DateOnly? DataDateOf(int day) => day <= 0 || _dates.Count == 0 ? null : _dates[Math.Min(day, _dates.Count) - 1];

    private sealed record StateFile(int ReleasedThroughDay, string LastNightlySlot, bool EndOfDataLogged, string UpdatedAtUtc);

    private bool Load()
    {
        var path = _settings.StateFile;

        if (path is null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path))
                ?? throw new InvalidDataException("empty file");
            _released = Math.Clamp(saved.ReleasedThroughDay, 0, _dates.Count);
            _lastSlot = DateOnly.ParseExact(saved.LastNightlySlot, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            _endLogged = saved.EndOfDataLogged;
            _logger.LogInformation("Release pointer loaded from {File}: days 1 to {Day} of {Total} released.", path, _released, _dates.Count);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or IOException)
        {
            // Never silently restart from the start day: that would replay days.
            throw new InvalidOperationException(
                $"The release state file '{path}' cannot be read ({ex.Message}). Fix or delete it (deleting resets to MOCK_START_DAY).", ex);
        }
    }

    private void Save()
    {
        var path = _settings.StateFile;

        if (path is null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            var body = new StateFile(_released, Iso(_lastSlot)!, _endLogged,
                _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            File.WriteAllText(temp, JsonSerializer.Serialize(body, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The pointer still works in memory; a restart would lose this change.
            _logger.LogError(ex, "Could not write the release state file {File}.", path);
        }
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static string? Iso(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
