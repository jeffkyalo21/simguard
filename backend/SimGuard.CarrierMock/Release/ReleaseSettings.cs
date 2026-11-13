using System.Globalization;

namespace SimGuard.CarrierMock.Release;

// Settings of the nightly release, read from plain environment variables
// (MOCK_*) so the carrier host's mock.env can set them. Bad values stop the
// start with a message that names the variable.
public sealed record ReleaseSettings(
    int StartDay,
    TimeOnly ReleaseTime,
    TimeZoneInfo Zone,
    string ZoneId,
    int DateShiftDays,
    string? StateFile,
    bool SchedulerEnabled,
    string? AdminKey)
{
    public const int DefaultStartDay = 80;

    public static ReleaseSettings FromConfiguration(IConfiguration config, IReadOnlyList<DateOnly> dataDates)
    {
        var zoneId = Text(config["MOCK_TIMEZONE"]) ?? "Africa/Nairobi";

        return new ReleaseSettings(
            ParseStartDay(config, dataDates),
            ParseReleaseTime(Text(config["MOCK_RELEASE_TIME"]) ?? "01:00"),
            FindZone(zoneId),
            zoneId,
            Shift(Text(config["MOCK_DATE_SHIFT_DAYS"])),
            Text(config["MOCK_STATE_FILE"]),
            Flag(Text(config["MOCK_SCHEDULER_ENABLED"]), "MOCK_SCHEDULER_ENABLED"),
            Text(config["MOCK_ADMIN_KEY"]));
    }

    // MOCK_START_DAY wins. Without it the older CarrierMock:StartDate still
    // works ("last", or a date: the days up to it), so stacks that set it keep
    // their behaviour. Without either: day 80 (or the last day of shorter data).
    private static int ParseStartDay(IConfiguration config, IReadOnlyList<DateOnly> dates)
    {
        var raw = Text(config["MOCK_START_DAY"]);

        if (raw is not null)
        {
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var day))
            {
                throw new InvalidOperationException($"MOCK_START_DAY must be a whole number of days (0 or more), not '{raw}'.");
            }

            return Math.Min(day, dates.Count);
        }

        var legacy = Text(config["CarrierMock:StartDate"]);

        if (legacy is not null)
        {
            if (string.Equals(legacy, "last", StringComparison.OrdinalIgnoreCase))
            {
                return dates.Count;
            }

            if (DateOnly.TryParseExact(legacy, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return dates.Count(d => d <= date);
            }
        }

        return Math.Min(DefaultStartDay, dates.Count);
    }

    private static TimeOnly ParseReleaseTime(string raw) =>
        TimeOnly.TryParseExact(raw, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : throw new InvalidOperationException($"MOCK_RELEASE_TIME must be HH:mm (for example 01:00), not '{raw}'.");

    private static int Shift(string? raw)
    {
        if (raw is null)
        {
            return 0;
        }

        if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var days) || days % 7 != 0)
        {
            throw new InvalidOperationException(
                $"MOCK_DATE_SHIFT_DAYS must be a whole multiple of 7 (so weekdays stay the same), not '{raw}'.");
        }

        return days;
    }

    private static bool Flag(string? raw, string name) => raw?.ToLowerInvariant() switch
    {
        null or "true" or "1" or "yes" => true,
        "false" or "0" or "no" => false,
        _ => throw new InvalidOperationException($"{name} must be true or false, not '{raw}'.")
    };

    // Africa/Nairobi has no daylight saving; if the time zone database is
    // missing in the image, UTC+3 is used for it. Any other unknown zone stops the start.
    private static TimeZoneInfo FindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (string.Equals(id, "Africa/Nairobi", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
            }

            throw new InvalidOperationException($"MOCK_TIMEZONE '{id}' is not a known time zone (use an IANA name such as Africa/Nairobi).", ex);
        }
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
