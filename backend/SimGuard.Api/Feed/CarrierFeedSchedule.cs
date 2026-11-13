using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimGuard.Api.Common;
using SimGuard.Api.Data;

namespace SimGuard.Api.Feed;

// CarrierFeed:DailySyncTime in CarrierFeed:TimeZone as points in time (UTC).
// A "slot" is one day's scheduled sync time. The server's clock zone is never used.
public sealed class FeedSchedule
{
    private FeedSchedule(TimeOnly time, TimeZoneInfo zone)
    {
        Time = time;
        Zone = zone;
    }

    public TimeOnly Time { get; }

    public TimeZoneInfo Zone { get; }

    public static FeedSchedule From(CarrierFeedOptions options) =>
        TryCreate(options.DailySyncTime, options.TimeZone, out var schedule, out var error)
            ? schedule
            : throw new InvalidOperationException(error);

    public static bool TryCreate(string? time, string? zoneId, out FeedSchedule schedule, out string error)
    {
        schedule = null!;
        error = string.Empty;

        if (!TimeOnly.TryParseExact(time?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            error = $"CarrierFeed:DailySyncTime must be a time like 01:30 (was '{time}').";
            return false;
        }

        var zone = FindZone(zoneId?.Trim());

        if (zone is null)
        {
            error = $"CarrierFeed:TimeZone must be an IANA time zone such as Africa/Nairobi (was '{zoneId}').";
            return false;
        }

        schedule = new FeedSchedule(t, zone);
        return true;
    }

    // A sync at or after the slot counts for that slot.
    public static bool IsDue(DateTime? lastSyncAtUtc, DateTime slotUtc) =>
        lastSyncAtUtc is null || lastSyncAtUtc.Value < slotUtc;

    // The most recent slot at or before nowUtc.
    public DateTime LatestSlotUtc(DateTime nowUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(nowUtc), Zone);
        var day = DateOnly.FromDateTime(local);
        var slot = SlotUtc(day);
        return slot <= AsUtc(nowUtc) ? slot : SlotUtc(day.AddDays(-1));
    }

    // The first slot after nowUtc.
    public DateTime NextSlotUtc(DateTime nowUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(nowUtc), Zone);
        var day = DateOnly.FromDateTime(local);
        var slot = SlotUtc(day);
        return slot > AsUtc(nowUtc) ? slot : SlotUtc(day.AddDays(1));
    }

    // The slot of a local calendar day. A time that does not exist that day
    // (clocks moved forward) runs at the first minute that does.
    public DateTime SlotUtc(DateOnly localDay)
    {
        var local = DateTime.SpecifyKind(localDay.ToDateTime(Time), DateTimeKind.Unspecified);

        for (var i = 0; i < 180 && Zone.IsInvalidTime(local); i++)
        {
            local = local.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, Zone);
    }

    public string Describe(DateTime slotUtc) =>
        $"{TimeZoneInfo.ConvertTimeFromUtc(AsUtc(slotUtc), Zone):yyyy-MM-dd HH:mm} {Zone.Id}";

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static TimeZoneInfo? FindZone(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
        }

        // Same fallback as the scoring schedule: Nairobi has no daylight saving.
        return id is "Africa/Nairobi" or "E. Africa Standard Time"
            ? TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3")
            : null;
    }
}

// What the daily service acts on; the database version is below, tests use a fake.
public interface IFeedSyncTarget
{
    // Enabled, configured feeds whose last sync was before the slot.
    Task<IReadOnlyList<int>> ListDueAsync(DateTime slotUtc, CancellationToken cancellationToken);

    Task RunAsync(int carrierId, CancellationToken cancellationToken);
}

public sealed class DbFeedSyncTarget(IServiceScopeFactory scopes, CarrierFeedRunner runner) : IFeedSyncTarget
{
    public async Task<IReadOnlyList<int>> ListDueAsync(DateTime slotUtc, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
        var slot = DateTime.SpecifyKind(slotUtc, DateTimeKind.Utc);
        return await db.Carriers.AsNoTracking()
            .Where(c => c.FeedEnabled && c.FeedKeySet && c.FeedBaseUrl != null)
            .Where(c => c.LastSyncAt == null || c.LastSyncAt < slot)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    // Skipped (null) when a Sync now for the carrier is running; that run counts.
    public Task RunAsync(int carrierId, CancellationToken cancellationToken) =>
        runner.RunAsync(carrierId, AuditWriter.SystemActor, cancellationToken);
}

public sealed record FeedCheck(DateTime SlotUtc, IReadOnlyList<int> CarrierIds, bool AtStart);

// Runs every enabled feed once a day at CarrierFeed:DailySyncTime in
// CarrierFeed:TimeZone. A feed is run for a slot only if it has not synced
// since that slot (a Sync now after the slot counts), so a slot is never
// synced twice. The first check after start (after StartupDelaySeconds) is
// the catch-up: if the API was down at the last slot and a feed has not
// synced since, it runs once; several missed nights still give one run (each
// run continues from the last synced day). On start it also marks runs left
// "running" by a previous process as failed.
public sealed class CarrierFeedWorker(
    IServiceScopeFactory scopes,
    IFeedSyncTarget target,
    IOptionsMonitor<CarrierFeedOptions> options,
    TimeProvider time,
    ILogger<CarrierFeedWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private DateTime? _handledSlot;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(options.CurrentValue.StartupDelaySeconds, 0)), time, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "The scheduled carrier feed check failed; it is tried again shortly.");
                }

                await Task.Delay(Tick, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    // One check: runs the due feeds when a slot has passed that this process
    // has not handled yet. Null when there was nothing to handle.
    public async Task<FeedCheck?> CheckAsync(CancellationToken cancellationToken)
    {
        var opts = options.CurrentValue;

        if (!opts.Enabled)
        {
            return null;
        }

        var schedule = FeedSchedule.From(opts);
        var slot = schedule.LatestSlotUtc(time.GetUtcNow().UtcDateTime);

        if (_handledSlot is { } handled && handled >= slot)
        {
            return null;
        }

        // Listed before the slot is marked handled, so a database error is retried.
        var ids = await target.ListDueAsync(slot, cancellationToken);
        var atStart = _handledSlot is null;
        _handledSlot = slot;

        if (ids.Count == 0)
        {
            if (atStart)
            {
                logger.LogInformation("Carrier feeds are up to date for the {Slot} sync; next sync {Next}.",
                    schedule.Describe(slot), schedule.Describe(schedule.NextSlotUtc(slot)));
            }

            return new FeedCheck(slot, ids, atStart);
        }

        logger.LogInformation(atStart
                ? "Catch-up carrier feed sync for the missed {Slot} sync: {Count} feed(s)."
                : "Scheduled carrier feed sync for {Slot}: {Count} feed(s).",
            schedule.Describe(slot), ids.Count);

        foreach (var id in ids)
        {
            await target.RunAsync(id, cancellationToken);
        }

        return new FeedCheck(slot, ids, atStart);
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
            await db.Carriers
                .Where(c => c.LastSyncStatus == FeedStatuses.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.LastSyncStatus, FeedStatuses.Failed)
                    .SetProperty(c => c.LastSyncMessage, "The run was interrupted by an API restart."),
                    cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not check for interrupted carrier feed runs.");
        }
    }
}
