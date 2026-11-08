using System.Globalization;
using Microsoft.Extensions.Options;
using SimGuard.Api.Common;
using SimGuard.Api.Imports;
using SimGuard.Api.Ml;

namespace SimGuard.Api.Scoring;

// Scoring:DailyRunTimeNairobi as a point in time. Africa/Nairobi has no
// daylight saving; if the time zone database is missing, UTC+3 is used.
public static class NairobiClock
{
    private static readonly TimeZoneInfo Zone = Find();

    private static TimeZoneInfo Find()
    {
        foreach (var id in new[] { "Africa/Nairobi", "E. Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
    }

    public static TimeOnly ParseRunTime(string? value) =>
        TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : new TimeOnly(2, 0);

    // The next time after nowUtc at which the local clock shows runTime.
    public static DateTime NextRunUtc(DateTime nowUtc, TimeOnly runTime)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), Zone);
        var candidate = local.Date.Add(runTime.ToTimeSpan());

        if (candidate <= local)
        {
            candidate = candidate.AddDays(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), Zone);
    }
}

// Enqueues the daily scheduled run (trigger schedule, actor system) at
// Scoring:DailyRunTimeNairobi while Scoring:Enabled is true. The options are
// read on every check (once a minute), so a value from a reloading
// configuration source applies without a restart.
public sealed class ScoringScheduler(
    ScoringQueue queue,
    IOptionsMonitor<ScoringOptions> options,
    ILogger<ScoringScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var next = NairobiClock.NextRunUtc(Timestamps.UtcNow(), NairobiClock.ParseRunTime(options.CurrentValue.DailyRunTimeNairobi));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Tick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var current = options.CurrentValue;
            var now = Timestamps.UtcNow();

            if (now < next)
            {
                continue;
            }

            next = NairobiClock.NextRunUtc(now, NairobiClock.ParseRunTime(current.DailyRunTimeNairobi));

            if (!current.Enabled)
            {
                continue;
            }

            try
            {
                var result = await queue.EnqueueAsync(
                    new ScoringRequest(ScoringTriggers.Schedule, false, null, null, null, null, AuditWriter.SystemActor),
                    stoppingToken);
                logger.LogInformation("Scheduled scoring run {RunId} queued (merged: {Merged}).", result.RunId, result.Merged);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not queue the scheduled scoring run.");
            }
        }
    }
}

// After a usage import commit: score the imported SIMs on every date the
// new rows can change, from the first imported date to the last one plus the
// lookback (a SIM-day reads the 14 days before it). Already scored SIM-days
// are skipped, so this scores only what became scorable.
public sealed class ScoringImportTrigger(
    ScoringQueue queue,
    IOptionsMonitor<ScoringOptions> options,
    ILogger<ScoringImportTrigger> logger)
{
    public Task<int?> AfterUsageImportAsync(ImportCoverage coverage, CancellationToken cancellationToken) =>
        coverage.Details.Count == 0 || coverage.FirstDate is null || coverage.LastDate is null
            ? Task.FromResult<int?>(null)
            : AfterUsageLoadedAsync(
                coverage.Details.Select(d => d.SimId).ToArray(), coverage.FirstDate.Value, coverage.LastDate.Value, cancellationToken);

    // Same for any source of usage rows (the carrier feed uses this).
    public async Task<int?> AfterUsageLoadedAsync(
        IReadOnlyCollection<int> simIds, DateOnly firstDate, DateOnly lastDate, CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled || simIds.Count == 0)
        {
            return null;
        }

        try
        {
            var result = await queue.EnqueueAsync(
                new ScoringRequest(
                    ScoringTriggers.Import,
                    false,
                    simIds,
                    firstDate,
                    lastDate.AddDays(MlLimits.ScoreLookbackDays - 1),
                    null,
                    AuditWriter.SystemActor),
                cancellationToken);
            return result.RunId;
        }
        catch (Exception ex)
        {
            // The import itself is committed; the next scheduled run scores it.
            logger.LogError(ex, "Could not queue a scoring run after a usage import.");
            return null;
        }
    }
}
