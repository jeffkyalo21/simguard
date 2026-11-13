using System.Globalization;

namespace SimGuard.CarrierMock;

// One SIM's usage for one day, as read from usage.csv.
public sealed record UsageRow(
    string Iccid,
    DateOnly Date,
    double DownloadMb,
    double UploadMb,
    long DataSessionCount,
    long DataDurationS,
    long HotspotSessions,
    long PublicApnSessions,
    long UniqueTowers,
    long GeoFlagCount,
    double PeakMb,
    double OffpeakMb,
    double TotalMb,
    long RoamingSessions,
    double BilledMb,
    string Tier,
    double VoiceMinutesUsed,
    long SmsCount,
    double VoiceMinutesRemaining,
    long SmsRemaining);

// The demo data, read once from files (no database):
//   sims.csv          iccid, carrier (the carrier assignment of import-demo-data)
//   usage.csv         the daily usage contract, one row per ICCID per day
//   sim_mapping.csv   optional, sim_id to iccid, only used for sessions
// plus an optional per-session file (synthetic_cdr_sessions.csv).
public sealed class MockData
{
    private readonly Dictionary<string, string> _carrierByIccid = new(StringComparer.Ordinal);
    private readonly Dictionary<DateOnly, List<UsageRow>> _rowsByDate = new();
    private readonly Dictionary<string, string> _simIdByIccid = new(StringComparer.Ordinal);
    private readonly string? _sessionsFile;

    public IReadOnlyCollection<string> CarrierNames { get; }
    public DateOnly? FirstDate { get; }
    public DateOnly? LastDate { get; }

    // Every date that has rows, oldest first. Day N of the release pointer is Dates[N - 1].
    public IReadOnlyList<DateOnly> Dates { get; }
    public int SimCount => _carrierByIccid.Count;
    public bool HasSessions => _sessionsFile is not null && _simIdByIccid.Count > 0;

    public MockData(string dataDir, string? sessionsFile)
    {
        var simsPath = Path.Combine(dataDir, "sims.csv");
        var usagePath = Path.Combine(dataDir, "usage.csv");

        if (!File.Exists(simsPath) || !File.Exists(usagePath))
        {
            throw new FileNotFoundException(
                $"The data folder '{dataDir}' must contain sims.csv and usage.csv. Set CarrierMock:DataDir.");
        }

        foreach (var cells in ReadCsv(simsPath, out var simHeader))
        {
            _carrierByIccid[Cell(cells, simHeader, "iccid")] = Cell(cells, simHeader, "carrier");
        }

        foreach (var c in ReadCsv(usagePath, out var h))
        {
            var iccid = Cell(c, h, "iccid");

            if (!_carrierByIccid.ContainsKey(iccid))
            {
                continue;
            }

            var date = DateOnly.ParseExact(Cell(c, h, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var row = new UsageRow(
                iccid, date,
                D(c, h, "total_download_mb"), D(c, h, "total_upload_mb"),
                L(c, h, "data_session_count"), L(c, h, "data_duration_s"),
                L(c, h, "hotspot_sessions"), L(c, h, "public_apn_sessions"),
                L(c, h, "unique_towers"), L(c, h, "geo_flag_count"),
                D(c, h, "peak_mb"), D(c, h, "offpeak_mb"), D(c, h, "total_mb"),
                L(c, h, "roaming_sessions"), D(c, h, "billed_mb"),
                Cell(c, h, "tier"),
                D(c, h, "voice_minutes_used"), L(c, h, "sms_count"),
                D(c, h, "voice_minutes_remaining"), L(c, h, "sms_remaining"));

            if (!_rowsByDate.TryGetValue(date, out var list))
            {
                _rowsByDate[date] = list = [];
            }

            list.Add(row);
        }

        Dates = _rowsByDate.Keys.Order().ToList();

        if (Dates.Count > 0)
        {
            FirstDate = Dates[0];
            LastDate = Dates[^1];
        }

        CarrierNames = _carrierByIccid.Values.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();

        var mappingPath = Path.Combine(dataDir, "sim_mapping.csv");

        if (File.Exists(mappingPath))
        {
            foreach (var c in ReadCsv(mappingPath, out var mh))
            {
                _simIdByIccid[Cell(c, mh, "iccid")] = Cell(c, mh, "sim_id");
            }
        }

        _sessionsFile = sessionsFile is not null && File.Exists(sessionsFile) ? sessionsFile : null;
    }

    public string? CarrierOf(string iccid) =>
        _carrierByIccid.TryGetValue(iccid, out var carrier) ? carrier : null;

    // Rows of one carrier on one day, ordered by ICCID so paging is stable.
    public List<UsageRow> Summary(string carrier, DateOnly date) =>
        !_rowsByDate.TryGetValue(date, out var rows)
            ? []
            : rows.Where(r => string.Equals(_carrierByIccid[r.Iccid], carrier, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Iccid, StringComparer.Ordinal)
                .ToList();

    // Streams the sessions file and keeps the lines of one SIM on one day
    // (a data date). startedAt is moved by shiftDays, like the summary dates.
    public List<object> Sessions(string iccid, DateOnly date, int shiftDays = 0)
    {
        var result = new List<object>();

        if (!HasSessions || !_simIdByIccid.TryGetValue(iccid, out var simId))
        {
            return result;
        }

        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var reader = new StreamReader(_sessionsFile!);
        var header = Split(reader.ReadLine() ?? string.Empty);

        var simPrefix = simId + ",";
        var iTime = Array.IndexOf(header, "session_timestamp");

        while (reader.ReadLine() is { } line)
        {
            // Cheap pre-check on the raw line (sim_id is the second column) so
            // the 98 MB file is not split line by line.
            var first = line.IndexOf(',');

            if (first < 0 || !line.AsSpan(first + 1).StartsWith(simPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var c = Split(line);

            if (c.Length != header.Length || !c[iTime].StartsWith(day, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(new
            {
                sessionId = Cell(c, header, "session_id"),
                iccid,
                startedAt = Shift(c[iTime], shiftDays),
                durationS = L(c, header, "session_duration"),
                type = Cell(c, header, "event_type"),
                downloadMb = D(c, header, "download_mb"),
                uploadMb = D(c, header, "upload_mb"),
                apn = Cell(c, header, "apn"),
                hotspot = Cell(c, header, "hotspot_flag") == "1",
                cellTowerId = Cell(c, header, "cell_tower_id"),
                callDestination = Cell(c, header, "call_destination"),
                roaming = Cell(c, header, "roaming_flag") == "1"
            });
        }

        return result;
    }

    // "yyyy-MM-dd hh:mm:ss..." with the date part moved by days.
    private static string Shift(string timestamp, int days)
    {
        if (days == 0 || timestamp.Length < 10
            || !DateOnly.TryParseExact(timestamp.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return timestamp;
        }

        return d.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + timestamp[10..];
    }

    private static string[] Split(string line) => line.TrimEnd('\r').Split(',');

    private static IEnumerable<string[]> ReadCsv(string path, out string[] header)
    {
        var lines = File.ReadLines(path).GetEnumerator();
        header = lines.MoveNext() ? Split(lines.Current.TrimStart('﻿')) : [];
        return Rows(lines);

        static IEnumerable<string[]> Rows(IEnumerator<string> lines)
        {
            while (lines.MoveNext())
            {
                if (lines.Current.Length > 0)
                {
                    yield return Split(lines.Current);
                }
            }
        }
    }

    private static string Cell(string[] cells, string[] header, string name)
    {
        var i = Array.IndexOf(header, name);
        return i < 0 || i >= cells.Length ? string.Empty : cells[i];
    }

    private static double D(string[] c, string[] h, string n) =>
        double.Parse(Cell(c, h, n), CultureInfo.InvariantCulture);

    private static long L(string[] c, string[] h, string n) =>
        long.Parse(Cell(c, h, n), CultureInfo.InvariantCulture);
}
