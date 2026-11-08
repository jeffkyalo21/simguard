using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Ml;
using SimGuard.Api.Models;

namespace SimGuard.Api.TierRecommendations;

// Executes one tier recommendation run (backend/docs/scoring-pipeline.md,
// "Tier recommendations"):
//
//  1. The billing cycle is the 30-day cycle holding the latest usage date.
//  2. SIMs with a tier and usage in that cycle are evaluated, except those
//     that already have an open recommendation.
//  3. /v1/recommend-tier is called in batches (contract maximum 200).
//  4. An upgrade or downgrade becomes an open recommendation, unless the SIM
//     had the same recommended tier dismissed in the last 90 days.
//     no_change and insufficient_history create nothing.
//
// Each batch is stored on its own; a failed batch stops the run (partial
// when something was evaluated, failed otherwise) and keeps what was stored.
public sealed class TierRecommendationRunner(
    SimGuardDbContext db,
    MlServiceClient ml,
    IOptions<TierRecommendationOptions> options,
    ILogger<TierRecommendationRunner> logger)
{
    private const int ErrorSummaryMax = 1000;

    private readonly TierRecommendationOptions _options = options.Value;

    public async Task RunAsync(int runId, CancellationToken cancellationToken)
    {
        int evaluated = 0, created = 0;
        int? cycle = null;
        string? error = null;

        try
        {
            var latest = await db.DailyUsage.MaxAsync(u => (DateOnly?)u.Date, cancellationToken);

            if (latest is null)
            {
                error = "No usage data to evaluate.";
            }
            else
            {
                cycle = BillingCycles.For(latest.Value);
                var cycleStart = BillingCycles.Start(cycle.Value);

                var sims = await db.Sims
                    .Where(s => s.Tier != null)
                    .Where(s => db.DailyUsage.Any(u => u.SimId == s.Id && u.Date >= cycleStart))
                    .Where(s => !db.TierRecommendations.Any(r => r.SimId == s.Id && r.Status == TierRecommendationStatuses.Open))
                    .OrderBy(s => s.Id)
                    .Select(s => s.Id)
                    .ToListAsync(cancellationToken);

                var batchSize = Math.Clamp(_options.BatchSize, 1, MlLimits.MaxRecommendSims);

                foreach (var batch in sims.Chunk(batchSize))
                {
                    MlRecommendResponse response;

                    try
                    {
                        response = await ml.RecommendTierAsync(batch, cycle.Value, cancellationToken);
                    }
                    catch (MlServiceException ex)
                    {
                        error = $"Batch of {batch.Length} SIMs starting at SIM {batch[0]}: {ex.Message}";
                        break;
                    }

                    created += await StoreAsync(runId, cycle.Value, batch, response, cancellationToken);
                    evaluated += batch.Length;
                    await SaveProgressAsync(runId, evaluated, created, cycle, cancellationToken);
                }
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
            logger.LogError(ex, "Tier recommendation run {RunId} failed.", runId);
            error = $"Unexpected error: {ex.GetType().Name}: {ex.Message}";
        }

        db.ChangeTracker.Clear();
        var run = await db.TierRecommendationRuns.SingleAsync(r => r.Id == runId, CancellationToken.None);
        run.BillingCycle = cycle;
        run.SimsEvaluated = evaluated;
        run.RecommendationsCreated = created;
        run.FinishedAt = Timestamps.UtcNow();
        run.Status = error is null
            ? TierRunStatuses.Succeeded
            : evaluated > 0 ? TierRunStatuses.Partial : TierRunStatuses.Failed;
        run.ErrorSummary = error is null ? null : error.Length > ErrorSummaryMax ? error[..ErrorSummaryMax] : error;
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation(
            "Tier recommendation run {RunId} {Status}: {Evaluated} SIMs evaluated, {Created} recommendations in {Seconds:F1} s.",
            runId, run.Status, evaluated, created, (run.FinishedAt.Value - run.StartedAt).TotalSeconds);
    }

    private async Task<int> StoreAsync(
        int runId,
        int cycle,
        int[] batch,
        MlRecommendResponse response,
        CancellationToken cancellationToken)
    {
        var sent = batch.ToHashSet();
        var changes = response.Results
            .Where(r => sent.Contains(r.SimId)
                        && r.Recommendation is TierRecommendationKinds.Upgrade or TierRecommendationKinds.Downgrade
                        && r.RecommendedTier is not null
                        && SimTiers.All.Contains(r.RecommendedTier)
                        && SimTiers.All.Contains(r.CurrentTier)
                        && r.RecommendedTier != r.CurrentTier)
            .GroupBy(r => r.SimId)
            .Select(g => g.First())
            .ToList();

        if (changes.Count == 0)
        {
            return 0;
        }

        var ids = changes.Select(c => c.SimId).ToArray();
        var since = Timestamps.UtcNow().AddDays(-TierRecommendationOptions.DismissedCooldownDays);

        var recentlyDismissed = (await db.TierRecommendations
                .Where(r => ids.Contains(r.SimId)
                            && r.Status == TierRecommendationStatuses.Dismissed
                            && r.DismissedAt >= since)
                .Select(r => new { r.SimId, r.RecommendedTier })
                .ToListAsync(cancellationToken))
            .Select(r => (r.SimId, r.RecommendedTier))
            .ToHashSet();

        var stillOpen = (await db.TierRecommendations
                .Where(r => ids.Contains(r.SimId) && r.Status == TierRecommendationStatuses.Open)
                .Select(r => r.SimId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var now = Timestamps.UtcNow();
        var count = 0;

        foreach (var c in changes)
        {
            if (stillOpen.Contains(c.SimId) || recentlyDismissed.Contains((c.SimId, c.RecommendedTier!)))
            {
                continue;
            }

            db.TierRecommendations.Add(new TierRecommendation
            {
                SimId = c.SimId,
                RunId = runId,
                CurrentTier = c.CurrentTier,
                RecommendedTier = c.RecommendedTier!,
                Recommendation = c.Recommendation,
                Reason = Reason(c, cycle),
                BillingCycle = cycle,
                ProjectedOverageMb = c.ProjectedOverageMb,
                DaysToDepletion = c.DaysToDepletion,
                Status = TierRecommendationStatuses.Open,
                CreatedAt = now
            });
            count++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return count;
    }

    private async Task SaveProgressAsync(int runId, int evaluated, int created, int? cycle, CancellationToken cancellationToken)
    {
        await db.TierRecommendationRuns
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SimsEvaluated, evaluated)
                .SetProperty(r => r.RecommendationsCreated, created)
                .SetProperty(r => r.BillingCycle, cycle), cancellationToken);
    }

    internal static string Reason(MlTierResult r, int cycle)
    {
        var mb = Math.Abs(r.ProjectedOverageMb ?? 0).ToString("N0", CultureInfo.InvariantCulture);
        var from = BillingCycles.Start(cycle).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return r.Recommendation == TierRecommendationKinds.Upgrade
            ? $"Forecast for the cycle starting {from}: {mb} MB over the {r.CurrentTier} bundle"
              + (r.DaysToDepletion is { } d ? $", bundle used up in {d} days." : ".")
            : $"Forecast for the cycle starting {from}: {mb} MB of the {r.CurrentTier} bundle unused.";
    }
}
