using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Models;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.TierRecommendations;

public sealed record TierRunStart(int? RunId, string? Conflict, DateTime? NextAllowedAt);

public sealed record TierSchedule(
    TierRecommendationRun? LastRun,
    int IntervalDays,
    int AutoRunGraceDays,
    DateTime? NextAllowedAt,
    bool AutomaticRunDue,
    bool Running);

// Starts tier recommendation runs: one at a time, never during a scoring
// run (both wait for PipelineLock), and at most one per IntervalDays.
// The interval counts from the start of the last run that did not fail.
public sealed class TierRecommendationService(
    IServiceScopeFactory scopes,
    PipelineLock pipelineLock,
    IOptions<TierRecommendationOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<TierRecommendationService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    // The task of the run started last; tests wait on it.
    public Task Current { get; private set; } = Task.CompletedTask;

    public static async Task<int> IntervalDaysAsync(SimGuardDbContext db, CancellationToken cancellationToken) =>
        await db.TierRecommendationSettings.Where(s => s.Id == 1).Select(s => (int?)s.IntervalDays)
            .FirstOrDefaultAsync(cancellationToken) ?? TierRecommendationOptions.DefaultIntervalDays;

    public async Task<TierSchedule> GetScheduleAsync(SimGuardDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var interval = await IntervalDaysAsync(db, cancellationToken);
        var grace = Math.Max(0, options.Value.AutoRunGraceDays);

        var last = await db.TierRecommendationRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var lastCounted = await db.TierRecommendationRuns.AsNoTracking()
            .Where(r => r.Status != TierRunStatuses.Failed)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTime?)r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var running = await db.TierRecommendationRuns.AnyAsync(r => r.Status == TierRunStatuses.Running, cancellationToken);
        var next = lastCounted?.AddDays(interval);

        // A first run is always manual: nothing is due before one exists.
        var due = lastCounted is not null && !running && now > lastCounted.Value.AddDays(interval + grace);

        return new TierSchedule(last, interval, grace, next, due, running);
    }

    public async Task<TierRunStart> TryStartAsync(
        string trigger,
        int? userId,
        string requestedBy,
        bool skipInterval,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<AuditWriter>();
            var now = Timestamps.UtcNow();
            var schedule = await GetScheduleAsync(db, now, cancellationToken);

            if (schedule.Running)
            {
                return new TierRunStart(null, "A tier recommendation run is already running.", schedule.NextAllowedAt);
            }

            if (!skipInterval && schedule.NextAllowedAt is { } next && now < next)
            {
                return new TierRunStart(
                    null,
                    $"The last tier recommendation run started less than {schedule.IntervalDays} days ago. "
                    + $"The next run is allowed from {next:yyyy-MM-dd} (UTC).",
                    next);
            }

            var run = new TierRecommendationRun
            {
                Trigger = trigger,
                Status = TierRunStatuses.Running,
                RequestedByUserId = userId,
                RequestedBy = requestedBy,
                StartedAt = now
            };

            db.TierRecommendationRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);

            var details = new { runId = run.Id, trigger, intervalDays = schedule.IntervalDays, intervalSkipped = skipInterval };

            if (trigger == TierRunTriggers.Automatic)
            {
                audit.AddSystem(AuditActions.TierRecommendationRunStarted, null, null, details);
            }
            else
            {
                audit.Add(AuditActions.TierRecommendationRunStarted, null, null, details);
            }

            await db.SaveChangesAsync(cancellationToken);

            var runId = run.Id;
            Current = Task.Run(() => ExecuteAsync(runId), CancellationToken.None);
            return new TierRunStart(runId, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteAsync(int runId)
    {
        var stopping = lifetime.ApplicationStopping;

        try
        {
            await pipelineLock.WaitAsync(stopping);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TierRecommendationRunner>().RunAsync(runId, stopping);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tier recommendation run {RunId} could not finish.", runId);
        }
        finally
        {
            pipelineLock.Release();
        }
    }
}

// Daily check for a due automatic run. On start it marks runs left
// "running" by a previous process as failed.
public sealed class TierRecommendationWorker(
    IServiceScopeFactory scopes,
    TierRecommendationService service,
    IOptions<TierRecommendationOptions> options,
    ILogger<TierRecommendationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var every = TimeSpan.FromHours(Math.Max(0.01, options.Value.CheckIntervalHours));

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
            await db.TierRecommendationRuns
                .Where(r => r.Status == TierRunStatuses.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, r => r.SimsEvaluated > 0 ? TierRunStatuses.Partial : TierRunStatuses.Failed)
                    .SetProperty(r => r.FinishedAt, Timestamps.UtcNow())
                    .SetProperty(r => r.ErrorSummary, "The API stopped during the run."), stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not check for interrupted tier recommendation runs.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
                var schedule = await service.GetScheduleAsync(db, Timestamps.UtcNow(), stoppingToken);

                if (schedule.AutomaticRunDue)
                {
                    var start = await service.TryStartAsync(
                        TierRunTriggers.Automatic, null, AuditWriter.SystemActor, false, stoppingToken);
                    logger.LogInformation("Automatic tier recommendation run: {Result}",
                        start.RunId is { } id ? $"started run {id}" : start.Conflict);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tier recommendation check failed.");
            }

            try
            {
                await Task.Delay(every, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

public static class TierRecommendationRegistration
{
    public static IServiceCollection AddTierRecommendations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TierRecommendationOptions>(configuration.GetSection(TierRecommendationOptions.Section));
        services.AddSingleton<TierRecommendationService>();
        services.AddScoped<TierRecommendationRunner>();
        services.AddHostedService<TierRecommendationWorker>();
        return services;
    }
}
