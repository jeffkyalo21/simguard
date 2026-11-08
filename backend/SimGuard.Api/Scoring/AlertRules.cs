using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Models;

namespace SimGuard.Api.Scoring;

// Alert rules, applied for every scored batch inside its transaction and in
// ascending date order, so a backfill replays history exactly as daily runs
// would have (backend/docs/scoring-pipeline.md, "Alert rules").
//
// The ML docs do not define a sustained pattern or a cooldown
// (ml-service/app/tiering.py leaves them to the backend), so the defaults
// set for issue #14 apply, configurable under "Scoring":
//
//  - Sustained on date D: at least SustainedMinDays (3) of the last
//    SustainedWindowDays (5) scored days up to D are flag or escalated, and
//    D itself is not clear.
//  - And at least Alerts:MinFlaggedDaysInWindow (1) of those window days is
//    flag. A SIM that is only escalated keeps its scores and gets no alert.
//  - One open alert (flagged or escalated) per SIM. A sustained day on a
//    SIM with an open alert updates that alert and never adds a second.
//  - Cleared alerts are never reopened by scoring. No new alert is raised
//    for a date the cleared alert already covered (up to the latest date
//    scored for the SIM when it was cleared). After a false positive there
//    is also a cooldown of FalsePositiveCooldownDays (7) days.
//  - Type: inactiveBilling when the stored daily usage on the window's
//    scored days holds at least Alerts:InactiveBillingMinDays (2) days with
//    total_mb at most InactiveBillingMaxUsageMb (1) and billed_mb at least
//    InactiveBillingMinBilledMb (100). Otherwise AlertTypeMap reads the
//    reasons of the riskiest day. Once inactiveBilling, an alert keeps it.
public sealed class AlertRules(SimGuardDbContext db, IOptions<ScoringOptions> options, IOptions<AlertOptions> alertOptions)
{
    private readonly ScoringOptions _options = options.Value;
    private readonly AlertOptions _alertOptions = alertOptions.Value;

    public sealed record Result(int Created, int Updated);

    private sealed record Day(DateOnly Date, decimal Probability, string Bucket, string Reasons);

    public int WindowDays => Math.Clamp(_options.SustainedWindowDays, 1, 60);
    public int MinDays => Math.Clamp(_options.SustainedMinDays, 1, WindowDays);
    public int CooldownDays => Math.Max(0, _options.FalsePositiveCooldownDays);
    public int MinFlaggedDays => Math.Clamp(_alertOptions.MinFlaggedDaysInWindow, 0, WindowDays);
    public int InactiveBillingMinDays => Math.Clamp(_alertOptions.InactiveBillingMinDays, 0, WindowDays);

    public async Task<Result> ApplyAsync(
        IDbContextTransaction transaction,
        DateOnly date,
        IReadOnlyCollection<int> scoredSimIds,
        string? modelVersion,
        CancellationToken cancellationToken)
    {
        if (scoredSimIds.Count == 0)
        {
            return new Result(0, 0);
        }

        var windows = await LoadWindowsAsync(transaction, date, scoredSimIds, cancellationToken);

        var sustained = windows
            .Where(w => w.Value.Count > 0
                        && w.Value[0].Date == date
                        && w.Value[0].Bucket != RiskBuckets.Clear
                        && w.Value.Count(d => d.Bucket != RiskBuckets.Clear) >= MinDays
                        && w.Value.Count(d => d.Bucket == RiskBuckets.Flag) >= MinFlaggedDays)
            .ToDictionary(w => w.Key, w => w.Value);

        if (sustained.Count == 0)
        {
            return new Result(0, 0);
        }

        var simIds = sustained.Keys.ToArray();

        var open = (await db.Alerts
                .Where(a => simIds.Contains(a.SimId) && a.Status != AlertStatuses.Cleared)
                .OrderBy(a => a.Id)
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.SimId)
            .ToDictionary(g => g.Key, g => g.First());

        var blockedUntil = await LoadBlockedUntilAsync(transaction, simIds, cancellationToken);
        var inactiveBilling = await LoadInactiveBillingAsync(transaction, sustained, cancellationToken);
        var now = Timestamps.UtcNow();
        int created = 0, updated = 0;

        foreach (var (simId, window) in sustained)
        {
            var raised = window.Where(d => d.Bucket != RiskBuckets.Clear).ToList();
            var status = raised.Any(d => d.Bucket == RiskBuckets.Flag) ? AlertStatuses.Flagged : AlertStatuses.Escalated;
            var top = raised.OrderByDescending(d => d.Probability).ThenByDescending(d => d.Date).First();
            var topScore = Math.Round(top.Probability, 2, MidpointRounding.AwayFromZero);

            var billedWhileInactive = inactiveBilling.Contains(simId);

            if (open.TryGetValue(simId, out var alert))
            {
                Update(alert, date, status, top, topScore, modelVersion, billedWhileInactive);
                updated++;
                continue;
            }

            if (blockedUntil.TryGetValue(simId, out var until) && date <= until)
            {
                continue;
            }

            using var reasons = JsonDocument.Parse(top.Reasons);

            db.Alerts.Add(new Alert
            {
                SimId = simId,
                Type = billedWhileInactive ? AlertTypes.InactiveBilling : AlertTypeMap.For(reasons.RootElement),
                RiskScore = topScore,
                RiskLevel = RiskLevels.ForNewAlert(status),
                Status = status,
                CreatedAt = now,
                Reasons = JsonDocument.Parse(top.Reasons),
                ModelVersion = modelVersion,
                DetectedOn = date,
                LastDetectedOn = date
            });
            created++;
        }

        return new Result(created, updated);
    }

    // Escalated may become flagged; flagged is never lowered. Score, reasons
    // and type follow the riskiest day seen, except that inactiveBilling from
    // the usage check is kept.
    private static void Update(
        Alert alert, DateOnly date, string status, Day top, decimal topScore, string? modelVersion, bool billedWhileInactive)
    {
        if (alert.DetectedOn is null || date < alert.DetectedOn)
        {
            alert.DetectedOn = date;
        }

        if (alert.LastDetectedOn is null || date > alert.LastDetectedOn)
        {
            alert.LastDetectedOn = date;
        }

        if (status == AlertStatuses.Flagged && alert.Status == AlertStatuses.Escalated)
        {
            alert.Status = AlertStatuses.Flagged;
            alert.RiskLevel = RiskLevels.High;
        }

        if (topScore > alert.RiskScore)
        {
            using var reasons = JsonDocument.Parse(top.Reasons);
            alert.RiskScore = topScore;
            alert.Reasons = JsonDocument.Parse(top.Reasons);
            alert.ModelVersion = modelVersion;

            if (alert.Type != AlertTypes.InactiveBilling)
            {
                alert.Type = AlertTypeMap.For(reasons.RootElement);
            }
        }

        if (billedWhileInactive)
        {
            alert.Type = AlertTypes.InactiveBilling;
        }
    }

    // SIMs whose stored usage on the window's scored days shows the SIM
    // billed while (almost) unused on at least InactiveBillingMinDays days.
    private async Task<HashSet<int>> LoadInactiveBillingAsync(
        IDbContextTransaction transaction,
        Dictionary<int, List<Day>> windows,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<int>();

        if (InactiveBillingMinDays == 0 || windows.Count == 0)
        {
            return result;
        }

        const string sql = """
            SELECT u.sim_id
            FROM unnest(@sim_ids, @dates) AS w(sim_id, date)
            JOIN daily_usage u ON u.sim_id = w.sim_id AND u.date = w.date
            WHERE u.total_mb <= @max_usage AND u.billed_mb >= @min_billed
            GROUP BY u.sim_id
            HAVING count(*) >= @min_days
            """;

        var pairs = windows.SelectMany(w => w.Value.Select(d => (SimId: w.Key, d.Date))).ToArray();

        await using var command = Command(sql, transaction);
        command.Parameters.Add(new NpgsqlParameter("sim_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = pairs.Select(p => p.SimId).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("dates", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = pairs.Select(p => p.Date).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("max_usage", NpgsqlDbType.Double) { Value = _alertOptions.InactiveBillingMaxUsageMb });
        command.Parameters.Add(new NpgsqlParameter("min_billed", NpgsqlDbType.Double) { Value = _alertOptions.InactiveBillingMinBilledMb });
        command.Parameters.Add(new NpgsqlParameter("min_days", NpgsqlDbType.Integer) { Value = InactiveBillingMinDays });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetInt32(0));
        }

        return result;
    }

    // The last WindowDays scored days up to the date, newest first.
    private async Task<Dictionary<int, List<Day>>> LoadWindowsAsync(
        IDbContextTransaction transaction,
        DateOnly date,
        IReadOnlyCollection<int> simIds,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.id, w.date, w.probability, w.bucket, w.reasons::text
            FROM unnest(@sim_ids) AS s(id)
            CROSS JOIN LATERAL (
                SELECT r.date, r.probability, r.bucket, r.reasons
                FROM risk_scores r
                WHERE r.sim_id = s.id AND r.date <= @date
                ORDER BY r.date DESC
                LIMIT @window
            ) w
            ORDER BY s.id, w.date DESC
            """;

        await using var command = Command(sql, transaction);
        command.Parameters.Add(new NpgsqlParameter("sim_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = simIds.ToArray() });
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
        command.Parameters.Add(new NpgsqlParameter("window", NpgsqlDbType.Integer) { Value = WindowDays });

        var result = new Dictionary<int, List<Day>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var simId = reader.GetInt32(0);

            if (!result.TryGetValue(simId, out var list))
            {
                result[simId] = list = [];
            }

            list.Add(new Day(reader.GetFieldValue<DateOnly>(1), reader.GetDecimal(2), reader.GetString(3), reader.GetString(4)));
        }

        return result;
    }

    // Per SIM, the last date on which a new alert may not be raised because
    // of a cleared alert: the later of its last detected date and the latest
    // date scored for the SIM before it was cleared, plus the cooldown for a
    // false positive.
    private async Task<Dictionary<int, DateOnly>> LoadBlockedUntilAsync(
        IDbContextTransaction transaction,
        int[] simIds,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.sim_id,
                   max(GREATEST(
                           a.last_detected_on,
                           (SELECT max(r.date) FROM risk_scores r
                            WHERE r.sim_id = a.sim_id AND r.created_at <= a.resolved_at))
                       + CASE WHEN a.resolution = 'falsePositive' THEN @cooldown ELSE 0 END)
            FROM alerts a
            WHERE a.status = 'cleared' AND a.sim_id = ANY(@sim_ids)
            GROUP BY a.sim_id
            """;

        await using var command = Command(sql, transaction);
        command.Parameters.Add(new NpgsqlParameter("sim_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = simIds });
        command.Parameters.Add(new NpgsqlParameter("cooldown", NpgsqlDbType.Integer) { Value = CooldownDays });

        var result = new Dictionary<int, DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(1))
            {
                result[reader.GetInt32(0)] = reader.GetFieldValue<DateOnly>(1);
            }
        }

        return result;
    }

    private NpgsqlCommand Command(string sql, IDbContextTransaction transaction) =>
        new(sql, (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
}
