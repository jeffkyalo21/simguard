using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Ml;
using SimGuard.Api.Models;

namespace SimGuard.Api.Scoring;

// Executes one scoring run (see backend/docs/scoring-pipeline.md):
//
//  1. GET /version for the model version. If the ML service is down the
//     run fails here and nothing changes.
//  2. Finds every scorable SIM-day in the run's scope: the SIM has usage on
//     each of the 15 days ending on that date (ML SCORE_LOOKBACK_DAYS).
//     Already scored SIM-days are skipped unless the run is forced.
//  3. Scores the dates in ascending order, in batches of at most
//     Scoring:BatchSize SIMs (contract maximum 5000) per /v1/score call.
//     Each batch is validated and then written in one transaction (upsert
//     by SIM and date, then the alert rules in AlertRules), so a failed
//     batch writes nothing.
//  4. Stops at the first failed batch: the run is partial when something
//     was scored, failed otherwise. Existing scores are never removed, and
//     the next run picks up the SIM-days that are still missing.
public sealed class ScoringRunner(
    SimGuardDbContext db,
    MlServiceClient ml,
    AuditWriter audit,
    AlertRules alertRules,
    IOptions<ScoringOptions> options,
    ILogger<ScoringRunner> logger)
{
    private const int ErrorSummaryMax = 1000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ScoringOptions _options = options.Value;

    private sealed record Candidate(DateOnly Date, int SimId);

    public async Task RunAsync(int runId, CancellationToken cancellationToken)
    {
        var run = await db.ScoringRuns.SingleAsync(r => r.Id == runId, cancellationToken);
        var counters = new Counters();
        string? error = null;

        try
        {
            var version = await ml.GetVersionAsync(cancellationToken);
            run.ModelVersion = version.ModelVersion();
            await db.SaveChangesAsync(cancellationToken);

            var candidates = await LoadCandidatesAsync(run, cancellationToken);
            var batchSize = Math.Clamp(_options.BatchSize, 1, MlLimits.MaxScoreSims);

            logger.LogInformation(
                "Scoring run {RunId} ({Trigger}): {Count} SIM-days to score on {Dates} dates.",
                run.Id,
                run.Trigger,
                candidates.Count,
                candidates.Select(c => c.Date).Distinct().Count());

            foreach (var day in candidates.GroupBy(c => c.Date).OrderBy(g => g.Key))
            {
                foreach (var batch in day.Select(c => c.SimId).Chunk(batchSize))
                {
                    error = await ScoreBatchAsync(run, day.Key, batch, counters, cancellationToken);

                    if (error is not null)
                    {
                        break;
                    }
                }

                if (error is not null)
                {
                    break;
                }

                counters.Dates++;
                counters.First ??= day.Key;
                counters.Last = day.Key;
                Apply(run, counters);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (MlServiceException ex)
        {
            error = ex.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error = "Stopped because the API is shutting down.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scoring run {RunId} failed.", runId);
            error = $"Unexpected error: {ex.GetType().Name}: {ex.Message}";
        }

        await FinishAsync(runId, counters, error);
    }

    // Returns null when the batch was stored, otherwise the error.
    private async Task<string?> ScoreBatchAsync(
        ScoringRun run,
        DateOnly date,
        int[] simIds,
        Counters counters,
        CancellationToken cancellationToken)
    {
        MlScoreResponse response;

        try
        {
            response = await ml.ScoreAsync(simIds, date, cancellationToken);
            Validate(response, simIds, date);
        }
        catch (MlServiceException ex)
        {
            counters.Failures++;
            return $"{date:yyyy-MM-dd} ({simIds.Length} SIMs): {ex.Message}";
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await UpsertAsync(transaction, date, response.Results, run.ModelVersion, run.Force, cancellationToken);
            var alerts = await alertRules.ApplyAsync(
                transaction, date, response.Results.Select(r => r.SimId).ToArray(), run.ModelVersion, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            counters.AlertsCreated += alerts.Created;
            counters.AlertsUpdated += alerts.Updated;
        }

        counters.SimDays += response.Results.Count;
        counters.Skipped += response.NotFound.Count;
        return null;
    }

    // Anything that does not match the request is rejected before a row is
    // written.
    internal static void Validate(MlScoreResponse response, IReadOnlyCollection<int> sent, DateOnly date)
    {
        var expected = sent.ToHashSet();
        var seen = new HashSet<int>();

        foreach (var r in response.Results)
        {
            string? problem = null;

            if (!expected.Contains(r.SimId)) problem = $"SIM {r.SimId} was not requested";
            else if (!seen.Add(r.SimId)) problem = $"SIM {r.SimId} appears twice";
            else if (r.Date != date) problem = $"SIM {r.SimId} has date {r.Date:yyyy-MM-dd}";
            else if (double.IsNaN(r.Probability) || r.Probability is < 0 or > 1) problem = $"SIM {r.SimId} has probability {r.Probability}";
            else if (!RiskBuckets.All.Contains(r.Bucket)) problem = $"SIM {r.SimId} has bucket '{r.Bucket}'";
            else if (r.Reasons is null) problem = $"SIM {r.SimId} has no reasons";

            if (problem is not null)
            {
                throw new MlServiceException(
                    MlErrorKind.InvalidResponse,
                    $"ML service returned an invalid score for {date:yyyy-MM-dd}: {problem}. Nothing was stored for this batch.");
            }
        }

        if (response.NotFound.Any(id => !expected.Contains(id) || seen.Contains(id)))
        {
            throw new MlServiceException(
                MlErrorKind.InvalidResponse,
                $"ML service returned an invalid notFound list for {date:yyyy-MM-dd}. Nothing was stored for this batch.");
        }
    }

    // Reasons in the alert and risk score contract
    // (backend/docs/api-alerts-risk-audit.md): real ML feature names,
    // direction up or down.
    internal static string ReasonsJson(IReadOnlyList<MlReason> reasons) =>
        JsonSerializer.Serialize(
            reasons.Select(r => new
            {
                feature = r.Feature,
                value = r.Value,
                contribution = r.Contribution,
                direction = r.Direction == "increases_risk" ? "up" : "down"
            }),
            Json);

    private async Task UpsertAsync(
        IDbContextTransaction transaction,
        DateOnly date,
        IReadOnlyList<MlSimScore> results,
        string? modelVersion,
        bool force,
        CancellationToken cancellationToken)
    {
        if (results.Count == 0)
        {
            return;
        }

        var conflict = force
            ? """
              DO UPDATE SET probability = EXCLUDED.probability, bucket = EXCLUDED.bucket,
                            reasons = EXCLUDED.reasons, model_version = EXCLUDED.model_version
              """
            : "DO NOTHING";

        var sql = $"""
            INSERT INTO risk_scores (sim_id, date, probability, bucket, reasons, model_version, created_at)
            SELECT t.sim_id, @date, t.probability, t.bucket, t.reasons::jsonb, @model_version, @now
            FROM unnest(@sim_ids, @probabilities, @buckets, @reasons) AS t(sim_id, probability, bucket, reasons)
            ON CONFLICT (sim_id, date) {conflict}
            """;

        await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());

        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
        command.Parameters.Add(new NpgsqlParameter("model_version", NpgsqlDbType.Varchar) { Value = (object?)modelVersion ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = Timestamps.UtcNow() });
        command.Parameters.Add(new NpgsqlParameter("sim_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = results.Select(r => r.SimId).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("probabilities", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = results.Select(r => Math.Round((decimal)r.Probability, 4)).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("buckets", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = results.Select(r => r.Bucket).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("reasons", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = results.Select(r => ReasonsJson(r.Reasons)).ToArray() });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Scorable SIM-days in scope, by date then SIM. Gaps and islands: within
    // a run of consecutive days, date minus row number is constant, and the
    // position in the run is the number of consecutive days ending on that
    // date.
    private async Task<List<Candidate>> LoadCandidatesAsync(ScoringRun run, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH numbered AS (
                SELECT u.sim_id, u.date,
                       u.date - (row_number() OVER (PARTITION BY u.sim_id ORDER BY u.date))::int AS run_key
                FROM daily_usage u
                WHERE @all_sims OR u.sim_id = ANY(@sim_ids)
            ),
            positioned AS (
                SELECT sim_id, date,
                       row_number() OVER (PARTITION BY sim_id, run_key ORDER BY date) AS run_pos
                FROM numbered
            )
            SELECT p.date, p.sim_id
            FROM positioned p
            WHERE p.run_pos >= @lookback
              AND (@from::date IS NULL OR p.date >= @from::date)
              AND (@to::date IS NULL OR p.date <= @to::date)
              AND (@force OR NOT EXISTS (
                  SELECT 1 FROM risk_scores r WHERE r.sim_id = p.sim_id AND r.date = p.date))
            ORDER BY p.date, p.sim_id
            """;

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(new NpgsqlParameter("all_sims", NpgsqlDbType.Boolean) { Value = run.ScopeSimIds is null });
            command.Parameters.Add(new NpgsqlParameter("sim_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = run.ScopeSimIds ?? [] });
            command.Parameters.Add(new NpgsqlParameter("lookback", NpgsqlDbType.Bigint) { Value = (long)MlLimits.ScoreLookbackDays });
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = (object?)run.ScopeFrom ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = (object?)run.ScopeTo ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("force", NpgsqlDbType.Boolean) { Value = run.Force });

            var list = new List<Candidate>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new Candidate(reader.GetFieldValue<DateOnly>(0), reader.GetInt32(1)));
            }

            return list;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // Writes the final state from a clean context, so a failed batch's
    // tracked changes can never be saved with it.
    private async Task FinishAsync(int runId, Counters counters, string? error)
    {
        db.ChangeTracker.Clear();
        var run = await db.ScoringRuns.SingleAsync(r => r.Id == runId, CancellationToken.None);
        Apply(run, counters);
        run.FinishedAt = Timestamps.UtcNow();

        if (error is null)
        {
            run.Status = ScoringRunStatuses.Succeeded;
        }
        else
        {
            run.Status = counters.SimDays > 0 ? ScoringRunStatuses.Partial : ScoringRunStatuses.Failed;
            run.ErrorSummary = error.Length > ErrorSummaryMax ? error[..ErrorSummaryMax] : error;
            audit.AddSystem(AuditActions.ScoringRunFailed, null, null, new
            {
                runId = run.Id,
                trigger = run.Trigger,
                status = run.Status,
                datesScored = run.DatesScored,
                simDaysScored = run.SimDaysScored,
                failures = run.Failures,
                errorSummary = run.ErrorSummary
            });
        }

        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation(
            "Scoring run {RunId} {Status}: {Dates} dates, {SimDays} SIM-days, {Failures} failures.",
            run.Id,
            run.Status,
            run.DatesScored,
            run.SimDaysScored,
            run.Failures);
    }

    private static void Apply(ScoringRun run, Counters c)
    {
        run.DatesScored = c.Dates;
        run.SimDaysScored = c.SimDays;
        run.SimDaysSkipped = c.Skipped;
        run.Failures = c.Failures;
        run.FirstDate = c.First;
        run.LastDate = c.Last;
        run.AlertsCreated = c.AlertsCreated;
        run.AlertsUpdated = c.AlertsUpdated;
    }

    internal sealed class Counters
    {
        public int Dates;
        public int SimDays;
        public int Skipped;
        public int Failures;
        public int AlertsCreated { get; set; }
        public int AlertsUpdated { get; set; }
        public DateOnly? First;
        public DateOnly? Last;
    }
}
