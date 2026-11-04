using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Data;

namespace SimGuard.Api.Scoring;

// Runs queued scoring runs one at a time, oldest first. On start it marks
// runs left "running" by a previous process as failed.
public sealed class ScoringWorker(
    IServiceScopeFactory scopes,
    ScoringQueue queue,
    PipelineLock pipelineLock,
    ILogger<ScoringWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not check for interrupted scoring runs.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await RunNextAsync(stoppingToken))
                {
                }

                await queue.WaitForWorkAsync(IdleCheck, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scoring worker error; retrying in a minute.");
                await Task.Delay(IdleCheck, stoppingToken).ContinueWith(_ => { });
            }
        }
    }

    // Returns false when nothing was queued.
    private async Task<bool> RunNextAsync(CancellationToken stoppingToken)
    {
        await pipelineLock.WaitAsync(stoppingToken);

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
            var runId = await queue.TakeNextAsync(db, stoppingToken);

            if (runId is null)
            {
                return false;
            }

            var runner = scope.ServiceProvider.GetRequiredService<ScoringRunner>();
            await runner.RunAsync(runId.Value, stoppingToken);
            return true;
        }
        finally
        {
            pipelineLock.Release();
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
        var now = Timestamps.UtcNow();

        var interrupted = await db.ScoringRuns
            .Where(r => r.Status == ScoringRunStatuses.Running)
            .ToListAsync(cancellationToken);

        foreach (var run in interrupted)
        {
            run.Status = run.SimDaysScored > 0 ? ScoringRunStatuses.Partial : ScoringRunStatuses.Failed;
            run.FinishedAt = now;
            run.ErrorSummary = "The API stopped during the run. Scores stored before that are kept; the next run continues.";
        }

        if (interrupted.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Marked {Count} interrupted scoring runs as stopped.", interrupted.Count);
        }
    }
}

public static class ScoringRegistration
{
    public static IServiceCollection AddScoringPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ScoringOptions>(configuration.GetSection(ScoringOptions.Section));
        services.AddSingleton<PipelineLock>();
        services.AddSingleton<ScoringQueue>();
        services.AddScoped<AlertRules>();
        services.AddScoped<ScoringRunner>();
        services.AddSingleton<ScoringImportTrigger>();
        services.AddHostedService<ScoringWorker>();
        services.AddHostedService<ScoringScheduler>();
        return services;
    }
}
