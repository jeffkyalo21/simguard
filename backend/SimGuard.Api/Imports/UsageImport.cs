using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace SimGuard.Api.Imports;

// Columns: iccid, date and the 18 daily fields of the ML daily usage
// contract (ml-service/docs/daily_usage_schema.md), in contract order.
// Upsert into daily_usage by (SIM, date) with binary COPY into a temporary
// table and one INSERT ... ON CONFLICT, the same pattern as
// ml-service/scripts/load_daily_usage.py. Unchanged rows are not rewritten.
public sealed class UsageImportHandler : IImportHandler
{
    // SCORE_LOOKBACK_DAYS in ml-service/app/config.py: a SIM is scorable
    // when its latest run of consecutive days is at least this long.
    public const int ScoreLookbackDays = 15;

    // Per SIM coverage entries returned; the summary counts cover all SIMs.
    public const int MaxCoverageDetails = 1000;

    private enum Kind { Double, Long, Tier }

    // The 18 value columns in contract order. Negative values are allowed
    // only where the ML contract allows them: the remaining voice minutes
    // and SMS go below zero when a bundle is overused.
    private static readonly (string Name, Kind Kind, bool AllowNegative)[] ValueColumns =
    [
        ("total_download_mb", Kind.Double, false),
        ("total_upload_mb", Kind.Double, false),
        ("data_session_count", Kind.Long, false),
        ("data_duration_s", Kind.Long, false),
        ("hotspot_sessions", Kind.Long, false),
        ("public_apn_sessions", Kind.Long, false),
        ("unique_towers", Kind.Long, false),
        ("geo_flag_count", Kind.Long, false),
        ("peak_mb", Kind.Double, false),
        ("offpeak_mb", Kind.Double, false),
        ("total_mb", Kind.Double, false),
        ("roaming_sessions", Kind.Long, false),
        ("billed_mb", Kind.Double, false),
        ("tier", Kind.Tier, false),
        ("voice_minutes_used", Kind.Double, false),
        ("sms_count", Kind.Long, false),
        ("voice_minutes_remaining", Kind.Double, true),
        ("sms_remaining", Kind.Long, true)
    ];

    private static readonly int DoubleCount = ValueColumns.Count(c => c.Kind == Kind.Double);
    private static readonly int LongCount = ValueColumns.Count(c => c.Kind == Kind.Long);

    public string Type => ImportTypes.Usage;

    public IReadOnlyList<string> Columns { get; } =
        ["iccid", "date", .. ValueColumns.Select(c => c.Name)];

    public IReadOnlyList<string[]> SampleRows { get; } =
    [
        ["8925402000000000017", "2025-01-01", "263.7242", "227.4051", "2", "478", "0", "0", "1", "0", "184.76", "306.37", "491.13", "0", "491.13", "field", "3.867", "4", "196.13", "131"],
        ["8925402000000000017", "2025-01-02", "120.5", "40.25", "3", "900", "0", "0", "2", "0", "100.5", "60.25", "160.75", "0", "160.75", "field", "0", "0", "196.13", "131"]
    ];

    public IImportJob CreateJob(ImportContext context) => new Job(context);

    private sealed class UsageRow(int simId, DateOnly date, double[] doubles, long[] longs, string tier)
    {
        public int SimId { get; } = simId;
        public DateOnly Date { get; } = date;
        public double[] Doubles { get; } = doubles;
        public long[] Longs { get; } = longs;
        public string Tier { get; } = tier;
    }

    private sealed class Job(ImportContext ctx) : IImportJob
    {
        private readonly Dictionary<(string Iccid, DateOnly Date), int> _seen = new();
        private readonly List<UsageRow> _rows = [];
        private Dictionary<string, (int Id, string? Tier)> _sims = [];

        public async Task PrepareAsync(CancellationToken cancellationToken)
        {
            var sims = await ctx.Db.Sims.AsNoTracking()
                .Select(s => new { s.Id, s.Iccid, s.Tier })
                .ToListAsync(cancellationToken);

            _sims = sims.ToDictionary(s => s.Iccid, s => (s.Id, s.Tier), StringComparer.Ordinal);
        }

        public bool AddRow(CsvRecord record)
        {
            var v = record.Values;
            var ok = true;
            var iccid = v[0];
            (int Id, string? Tier) sim = default;

            if (iccid.Length == 0)
            {
                ctx.Errors.Add(record.RowNumber, "iccid", "iccid is required.");
                ok = false;
            }
            else if (!_sims.TryGetValue(iccid, out sim))
            {
                ctx.Errors.Add(record.RowNumber, "iccid", $"There is no SIM with ICCID {iccid}. Import the SIM first.");
                ok = false;
            }

            if (!ImportCells.Date(ctx, record, "date", v[1], out var date))
            {
                ok = false;
            }
            else if (iccid.Length > 0 && !_seen.TryAdd((iccid, date), record.RowNumber))
            {
                ctx.Errors.Add(record.RowNumber, "date",
                    $"ICCID {iccid} on {date:yyyy-MM-dd} is already in row {_seen[(iccid, date)]} of this file.");
                ok = false;
            }

            var doubles = new double[DoubleCount];
            var longs = new long[LongCount];
            var tier = string.Empty;
            int d = 0, l = 0;

            for (var i = 0; i < ValueColumns.Length; i++)
            {
                var (name, kind, allowNegative) = ValueColumns[i];
                var cell = v[i + 2];

                switch (kind)
                {
                    case Kind.Double:
                        ok &= ImportCells.Number(ctx, record, name, cell, allowNegative, out doubles[d++]);
                        break;
                    case Kind.Long:
                        ok &= ImportCells.Integer(ctx, record, name, cell, allowNegative, out longs[l++]);
                        break;
                    default:
                        if (cell.Length == 0)
                        {
                            ctx.Errors.Add(record.RowNumber, name, $"{name} is required.");
                            ok = false;
                        }
                        else if (!ImportCells.Tier(ctx, record, name, cell, out tier))
                        {
                            ok = false;
                        }
                        break;
                }
            }

            if (!ok)
            {
                return false;
            }

            if (sim.Tier is not null && sim.Tier != tier)
            {
                ctx.Warnings.Add(record.RowNumber, "tier",
                    $"tier '{tier}' differs from the SIM's tier '{sim.Tier}'. The row is stored as given; scoring uses the SIM's tier.");
            }

            _rows.Add(new UsageRow(sim.Id, date, doubles, longs, tier));
            return true;
        }

        public async Task<ImportApplyResult> ApplyAsync(CancellationToken cancellationToken)
        {
            var connection = (NpgsqlConnection)ctx.Db.Database.GetDbConnection();
            var transaction = ctx.Db.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("The usage import must run inside a transaction.");

            var columns = string.Join(", ", ValueColumns.Select(c => c.Name));
            var allColumns = $"sim_id, date, {columns}";

            // Dropped at the end of the transaction, also when a preview
            // rolls back.
            await ExecuteAsync(connection, transaction,
                $"CREATE TEMP TABLE import_usage ON COMMIT DROP AS SELECT {allColumns} FROM daily_usage WITH NO DATA",
                cancellationToken);

            await using (var writer = await connection.BeginBinaryImportAsync(
                             $"COPY import_usage ({allColumns}) FROM STDIN (FORMAT BINARY)", cancellationToken))
            {
                foreach (var row in _rows)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(row.SimId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Date, NpgsqlDbType.Date, cancellationToken);

                    int d = 0, l = 0;

                    foreach (var (_, kind, _) in ValueColumns)
                    {
                        switch (kind)
                        {
                            case Kind.Double:
                                await writer.WriteAsync(row.Doubles[d++], NpgsqlDbType.Double, cancellationToken);
                                break;
                            case Kind.Long:
                                await writer.WriteAsync(row.Longs[l++], NpgsqlDbType.Bigint, cancellationToken);
                                break;
                            default:
                                await writer.WriteAsync(row.Tier, NpgsqlDbType.Varchar, cancellationToken);
                                break;
                        }
                    }
                }

                await writer.CompleteAsync(cancellationToken);
            }

            var updates = string.Join(", ", ValueColumns.Select(c => $"{c.Name} = EXCLUDED.{c.Name}"));
            var current = string.Join(", ", ValueColumns.Select(c => $"daily_usage.{c.Name}"));
            var incoming = string.Join(", ", ValueColumns.Select(c => $"EXCLUDED.{c.Name}"));

            // xmax = 0 marks a freshly inserted row. The WHERE skips rows
            // whose values are all the same, so they are neither rewritten
            // nor returned.
            var upsert = $"""
                WITH written AS (
                    INSERT INTO daily_usage ({allColumns})
                    SELECT {allColumns} FROM import_usage
                    ON CONFLICT (sim_id, date) DO UPDATE SET {updates}
                    WHERE ({current}) IS DISTINCT FROM ({incoming})
                    RETURNING (xmax = 0) AS inserted
                )
                SELECT count(*) FILTER (WHERE inserted), count(*) FILTER (WHERE NOT inserted) FROM written
                """;

            int inserted, updated;

            await using (var command = CreateCommand(connection, transaction, upsert))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                inserted = (int)reader.GetInt64(0);
                updated = (int)reader.GetInt64(1);
            }

            var coverage = await CoverageAsync(connection, transaction, cancellationToken);
            return new ImportApplyResult(inserted, updated, _rows.Count - inserted - updated, coverage);
        }

        // Coverage of every SIM in the file over all its stored days (after
        // this import), with the gaps and islands method: consecutive dates
        // minus their row number are constant within one run of days.
        private async Task<ImportCoverage> CoverageAsync(
            NpgsqlConnection connection,
            DbTransaction transaction,
            CancellationToken cancellationToken)
        {
            const string sql = """
                WITH affected AS (SELECT DISTINCT sim_id FROM import_usage),
                numbered AS (
                    SELECT u.sim_id, u.date,
                           u.date - (row_number() OVER (PARTITION BY u.sim_id ORDER BY u.date))::int AS run_key
                    FROM daily_usage u
                    JOIN affected a ON a.sim_id = u.sim_id
                ),
                runs AS (
                    SELECT sim_id, min(date) AS run_start, max(date) AS run_end, count(*)::int AS run_days
                    FROM numbered
                    GROUP BY sim_id, run_key
                ),
                per_sim AS (
                    SELECT sim_id,
                           min(run_start) AS first_date,
                           max(run_end) AS last_date,
                           sum(run_days)::int AS days,
                           (count(*) - 1)::int AS gap_count,
                           (array_agg(run_days ORDER BY run_end DESC))[1] AS latest_run_days
                    FROM runs
                    GROUP BY sim_id
                )
                SELECT p.sim_id, s.iccid, p.first_date, p.last_date, p.days, p.gap_count,
                       (p.last_date - p.first_date + 1 - p.days)::int AS missing_days,
                       p.latest_run_days
                FROM per_sim p
                JOIN sims s ON s.id = p.sim_id
                ORDER BY p.sim_id
                """;

            var all = new List<SimCoverage>();

            await using (var command = CreateCommand(connection, transaction, sql))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var latest = reader.GetInt32(7);
                    all.Add(new SimCoverage(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetFieldValue<DateOnly>(2),
                        reader.GetFieldValue<DateOnly>(3),
                        reader.GetInt32(4),
                        reader.GetInt32(5),
                        reader.GetInt32(6),
                        latest,
                        latest >= ScoreLookbackDays));
                }
            }

            // Gaps and short history are warnings, never errors.
            foreach (var sim in all)
            {
                if (sim.GapCount > 0)
                {
                    ctx.Warnings.Add(null, null,
                        $"SIM {sim.Iccid} has {sim.GapCount} gap(s): {sim.MissingDays} missing day(s) between {sim.FirstDate:yyyy-MM-dd} and {sim.LastDate:yyyy-MM-dd}.");
                }

                if (!sim.Scorable)
                {
                    ctx.Warnings.Add(null, null,
                        $"SIM {sim.Iccid} has {sim.LatestRunDays} consecutive day(s) up to {sim.LastDate:yyyy-MM-dd}; scoring needs {ScoreLookbackDays}.");
                }
            }

            // Problem SIMs first, so a capped list still shows them.
            var details = all
                .OrderBy(s => s.Scorable)
                .ThenByDescending(s => s.GapCount > 0)
                .ThenBy(s => s.SimId)
                .Take(MaxCoverageDetails)
                .ToList();

            return new ImportCoverage(
                all.Count,
                all.Count == 0 ? null : all.Min(s => s.FirstDate),
                all.Count == 0 ? null : all.Max(s => s.LastDate),
                ScoreLookbackDays,
                all.Count(s => s.Scorable),
                all.Count(s => !s.Scorable),
                all.Count(s => s.GapCount > 0),
                details);
        }

        private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, DbTransaction transaction, string sql) =>
            new(sql, connection, (NpgsqlTransaction)transaction) { CommandTimeout = 300 };

        private static async Task ExecuteAsync(
            NpgsqlConnection connection, DbTransaction transaction, string sql, CancellationToken cancellationToken)
        {
            await using var command = CreateCommand(connection, transaction, sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
