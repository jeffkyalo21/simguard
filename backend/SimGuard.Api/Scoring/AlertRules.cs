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
//  - One open alert (flagged or escalated) per SIM. A sustained day on a
//    SIM with an open alert updates that alert and never adds a second.
//  - Cleared alerts are never reopened by scoring. No new alert is raised
//    for a date the cleared alert already covered (up to the latest date
//    scored for the SIM when it was cleared). After a false positive there
//    is also a cooldown of FalsePositiveCooldownDays (7) days.
public sealed class AlertRules(SimGuardDbContext db, IOptions<ScoringOptions> options)
{
    private readonly ScoringOptions _options = options.Value;

    public sealed record Result(int Created, int Updated);

    private sealed record Day(DateOnly Date, decimal Probability, string Bucket, string Reasons);

    public int WindowDays => Math.Clamp(_options.SustainedWindowDays, 1, 60);
    public int MinDays => Math.Clamp(_options.SustainedMinDays, 1, WindowDays);
    public int CooldownDays => Math.Max(0, _options.FalsePositiveCooldownDays);

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
                        && w.Value.Count(d => d.Bucket != RiskBuckets.Clear) >= MinDays)
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
        var now = Timestamps.UtcNow();
        int created = 0, updated = 0;

        foreach (var (simId, window) in sustained)
        {
            var raised = window.Where(d => d.Bucket != RiskBuckets.Clear).ToList();
            var status = raised.Any(d => d.Bucket == RiskBuckets.Flag) ? AlertStatuses.Flagged : AlertStatuses.Escalated;
            var top = raised.OrderByDescending(d => d.Probability).ThenByDescending(d => d.Date).First();
            var topScore = Math.Round(top.Probability, 2, MidpointRounding.AwayFromZero);

            if (open.TryGetValue(simId, out var alert))
            {
                Update(alert, date, status, top, topScore, modelVersion);
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
                Type = AlertTypeMap.For(reasons.RootElement),
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
    // and type follow the riskiest day seen.
    private static void Update(Alert alert, DateOnly date, string status, Day top, decimal topScore, string? modelVersion)
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
            alert.Type = AlertTypeMap.For(reasons.RootElement);
            alert.Reasons = JsonDocument.Parse(top.Reasons);
            alert.ModelVersion = modelVersion;
        }
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
