using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SimGuard.CarrierMock.Tests;

// A clock the tests move by hand.
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

// A small data folder: two carriers, two SIMs each, one usage row per SIM
// per day from 2025-01-01 (a Wednesday) for the given number of days.
public sealed class TestData : IDisposable
{
    public const string KeyA = "key-a-0123456789abcdef";
    public const string KeyB = "key-b-0123456789abcdef";
    public static readonly DateOnly FirstDate = new(2025, 1, 1);

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "sg-mock-test-" + Guid.NewGuid().ToString("N"));
    public string StateFile => Path.Combine(Dir, "state", "release-state.json");
    public int Days { get; }

    public TestData(int days)
    {
        Days = days;
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "sims.csv"),
            "iccid,phone_number,carrier,tier,status\n" +
            string.Concat(Enumerable.Range(1, 4).Select(n => $"{Iccid(n)},{n},{(n <= 2 ? "CarrierA" : "CarrierB")},basic,active\n")));

        var usage = new StringBuilder("iccid,date,total_download_mb,total_upload_mb,data_session_count,data_duration_s,hotspot_sessions,public_apn_sessions,unique_towers,geo_flag_count,peak_mb,offpeak_mb,total_mb,roaming_sessions,billed_mb,tier,voice_minutes_used,sms_count,voice_minutes_remaining,sms_remaining\n");

        for (var d = 0; d < days; d++)
        {
            var date = FirstDate.AddDays(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            for (var sim = 1; sim <= 4; sim++)
            {
                usage.Append(CultureInfo.InvariantCulture, $"{Iccid(sim)},{date},10,5,2,60,0,0,1,0,7,8,{15 + d},0,{15 + d},basic,1.5,2,100,50\n");
            }
        }

        File.WriteAllText(Path.Combine(Dir, "usage.csv"), usage.ToString());
    }

    public static string Iccid(int n) => "89254" + n.ToString("D14", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

// Collects log messages so a test can count them.
public sealed class ListLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    public int Count(string text) => Entries.Count(e => e.Message.Contains(text, StringComparison.Ordinal));
}
