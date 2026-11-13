namespace SimGuard.CarrierMock.Release;

// Checks every 30 seconds whether a nightly slot has started (in
// MOCK_TIMEZONE, at MOCK_RELEASE_TIME). The first check runs at start, so
// a night missed during downtime is released at the next start.
public sealed class ReleaseScheduler(ReleaseState state, ReleaseSettings settings, TimeProvider time, ILogger<ReleaseScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.SchedulerEnabled)
        {
            logger.LogInformation("Nightly release is off (MOCK_SCHEDULER_ENABLED=false).");
            return;
        }

        logger.LogInformation("Nightly release at {Time} {Zone}.", settings.ReleaseTime.ToString("HH:mm"), settings.ZoneId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                state.Tick();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nightly release check failed.");
            }

            try
            {
                await Task.Delay(Tick, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
