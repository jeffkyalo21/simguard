using Microsoft.AspNetCore.DataProtection;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Imports;
using SimGuard.Api.Models;
using SimGuard.Api.Scoring;

namespace SimGuard.Api.Feed;

public static class FeedStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public static class ImportSources
{
    public const string Csv = "csv";
    public const string CarrierFeed = "carrier feed";
}

public sealed record FeedRunResult(bool Started, string Status, string Message, int Days, int Imported, int Rejected);

// Pulls daily usage from one carrier's feed into daily_usage.
//
// Resumes the day after Carrier.LastSyncDate and goes day by day. Each day is
// saved in one transaction together with the cursor, so a failure on day N
// leaves days before N imported and the cursor on the last good day. Rows are
// matched to the carrier's SIMs by ICCID; unknown ICCIDs are counted as
// rejected, not as errors. Upserts by (SIM, date), so running a day twice
// adds no rows. See docs/carrier-feed.md.
public sealed class CarrierFeedSync(
    SimGuardDbContext db,
    CarrierFeedClient client,
    ICarrierSecretStore secrets,
    ScoringImportTrigger scoring,
    IOptionsMonitor<CarrierFeedOptions> options,
    ILogger<CarrierFeedSync> logger)
{
    private const int MessageMax = 500;

    // The caller already holds the carrier's run lock.
    public async Task<FeedRunResult> RunAsync(int carrierId, string requestedBy, int? userId, CancellationToken cancellationToken)
    {
        var opts = options.CurrentValue;
        var carrier = await db.Carriers.FirstOrDefaultAsync(c => c.Id == carrierId, cancellationToken);

        if (carrier is null)
        {
            return new FeedRunResult(false, FeedStatuses.Failed, "Carrier not found.", 0, 0, 0);
        }

        var startedAt = Timestamps.UtcNow();
        var apiKey = await secrets.GetAsync(carrierId, cancellationToken);

        if (string.IsNullOrEmpty(carrier.FeedBaseUrl) || string.IsNullOrEmpty(apiKey))
        {
            return await FinishAsync(carrier, startedAt, requestedBy, userId, FeedStatuses.Failed,
                "The feed is not set up. Save a feed address and an API key first.", 0, 0, 0, 0, 0, 0, 0, null, null);
        }

        carrier.LastSyncStatus = FeedStatuses.Running;
        carrier.LastSyncAt = startedAt;
        carrier.LastSyncMessage = null;
        await db.SaveChangesAsync(cancellationToken);

        var today = DateOnly.FromDateTime(Timestamps.UtcNow());
        var next = carrier.LastSyncDate?.AddDays(1)
            ?? carrier.FeedStartDate
            ?? today.AddDays(-Math.Max(opts.DefaultLookbackDays, 1));
        var last = today;
        var cap = next.AddDays(Math.Max(opts.MaxDaysPerRun, 1) - 1);

        if (cap < last)
        {
            last = cap;
        }

        var sims = await db.Sims.AsNoTracking()
            .Where(s => s.CarrierId == carrierId)
            .Select(s => new { s.Id, s.Iccid, s.Tier })
            .ToDictionaryAsync(s => s.Iccid, s => (s.Id, s.Tier), StringComparer.Ordinal, cancellationToken);

        var days = 0;
        int fetched = 0, valid = 0, rejected = 0, inserted = 0, updated = 0, unchanged = 0;
        var simIds = new HashSet<int>();
        DateOnly? first = null, lastDone = null;
        string? failure = null;
        string? note = null;

        try
        {
            for (var day = next; day <= last; day = day.AddDays(1))
            {
                var rows = await FetchDayAsync(carrier.FeedBaseUrl, apiKey, day, opts, cancellationToken);

                if (rows is null)
                {
                    note = $"Caught up: the carrier has no data for {day:yyyy-MM-dd} yet.";
                    break;
                }

                if (rows.Count == 0)
                {
                    note = $"The carrier returned no rows for {day:yyyy-MM-dd}. It will be tried again on the next run.";
                    break;
                }

                var result = await SaveDayAsync(carrier, day, rows, sims, cancellationToken);
                days++;
                fetched += rows.Count;
                valid += result.Valid;
                rejected += rows.Count - result.Valid;
                inserted += result.Inserted;
                updated += result.Updated;
                unchanged += result.Unchanged;
                simIds.UnionWith(result.SimIds);
                first ??= day;
                lastDone = day;
            }
        }
        catch (FeedException ex)
        {
            failure = ex.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            failure = "The run was interrupted because the API is stopping.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Carrier feed run for carrier {CarrierId} failed.", carrierId);
            failure = "SimGuard could not save the carrier's usage. See the API log.";
        }

        // The tracked carrier may hold a partly saved day after an error.
        db.ChangeTracker.Clear();
        carrier = await db.Carriers.FirstAsync(c => c.Id == carrierId, CancellationToken.None);

        string status;
        string message;

        if (failure is not null)
        {
            status = FeedStatuses.Failed;
            message = days == 0
                ? failure
                : $"{failure} Imported {days} day(s) before it failed; the next run continues after {lastDone:yyyy-MM-dd}.";
        }
        else
        {
            status = FeedStatuses.Succeeded;
            message = days == 0
                ? note ?? "Already up to date."
                : $"Imported {days} day(s) up to {lastDone:yyyy-MM-dd}: {valid} row(s), {rejected} rejected." + (note is null ? string.Empty : " " + note);
        }

        var result2 = await FinishAsync(carrier, startedAt, requestedBy, userId, status, message,
            days, fetched, valid, rejected, inserted, updated, unchanged, first, lastDone);

        if (simIds.Count > 0 && first is not null && lastDone is not null)
        {
            await scoring.AfterUsageLoadedAsync(simIds, first.Value, lastDone.Value, CancellationToken.None);
        }

        return result2;
    }

    // Null when the carrier has not published the date yet.
    private async Task<List<FeedRow>?> FetchDayAsync(
        string baseUrl, string apiKey, DateOnly day, CarrierFeedOptions opts, CancellationToken cancellationToken)
    {
        var rows = new List<FeedRow>();
        var pageSize = Math.Clamp(opts.PageSize, 1, 1000);

        for (var page = 1; page <= 1000; page++)
        {
            FeedPage result;

            try
            {
                result = await client.GetSummaryPageAsync(baseUrl, apiKey, day, page, pageSize, true, cancellationToken);
            }
            catch (FeedDateNotAvailableException)
            {
                return null;
            }

            rows.AddRange(result.Rows);

            if (result.Rows.Count == 0 || rows.Count >= result.Total)
            {
                break;
            }
        }

        return rows;
    }

    private sealed record DayResult(int Valid, int Inserted, int Updated, int Unchanged, IReadOnlyCollection<int> SimIds);

    private async Task<DayResult> SaveDayAsync(
        Carrier carrier,
        DateOnly day,
        List<FeedRow> rows,
        Dictionary<string, (int Id, string? Tier)> sims,
        CancellationToken cancellationToken)
    {
        var usable = new Dictionary<int, DailyUsage>();

        foreach (var row in rows)
        {
            var iccid = row.Iccid?.Trim();

            if (string.IsNullOrEmpty(iccid) || !sims.TryGetValue(iccid, out var sim))
            {
                continue;
            }

            var mapped = Map(row, sim.Id, sim.Tier, day);

            if (mapped is not null)
            {
                usable[sim.Id] = mapped;
            }
        }

        var ids = usable.Keys.ToList();
        var existing = await db.DailyUsage
            .Where(u => u.Date == day && ids.Contains(u.SimId))
            .ToDictionaryAsync(u => u.SimId, cancellationToken);

        int inserted = 0, updated = 0, unchanged = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var (simId, incoming) in usable)
        {
            if (!existing.TryGetValue(simId, out var current))
            {
                db.DailyUsage.Add(incoming);
                inserted++;
            }
            else if (CopyIfDifferent(current, incoming))
            {
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        carrier.LastSyncDate = day;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new DayResult(usable.Count, inserted, updated, unchanged, ids);
    }

    // Null when the row cannot be stored (counted as rejected).
    internal static DailyUsage? Map(FeedRow row, int simId, string? simTier, DateOnly day)
    {
        if (row.DataMb is null or < 0)
        {
            return null;
        }

        // A row for another date than the one asked for is not stored.
        if (row.Date is not null
            && !(DateOnly.TryParseExact(row.Date, "yyyy-MM-dd", out var rowDate) && rowDate == day))
        {
            return null;
        }

        var tier = row.Tier is not null && SimTiers.All.Contains(row.Tier) ? row.Tier : simTier;

        if (tier is null)
        {
            return null;
        }

        double NonNegative(double? value) => value is > 0 ? value.Value : 0;
        long NonNegativeLong(long? value) => value is > 0 ? value.Value : 0;

        return new DailyUsage
        {
            SimId = simId,
            Date = day,
            TotalMb = row.DataMb.Value,
            TotalDownloadMb = row.DownloadMb is null ? row.DataMb.Value : NonNegative(row.DownloadMb),
            TotalUploadMb = NonNegative(row.UploadMb),
            DataSessionCount = NonNegativeLong(row.DataSessionCount),
            DataDurationS = NonNegativeLong(row.DataDurationS),
            HotspotSessions = NonNegativeLong(row.HotspotSessions),
            PublicApnSessions = NonNegativeLong(row.PublicApnSessions),
            UniqueTowers = NonNegativeLong(row.UniqueTowers),
            GeoFlagCount = NonNegativeLong(row.GeoFlagCount),
            PeakMb = NonNegative(row.PeakMb),
            OffpeakMb = NonNegative(row.OffpeakMb),
            RoamingSessions = NonNegativeLong(row.RoamingSessions),
            BilledMb = row.BilledMb is null ? row.DataMb.Value : NonNegative(row.BilledMb),
            Tier = tier,
            VoiceMinutesUsed = NonNegative(row.VoiceMinutes),
            SmsCount = NonNegativeLong(row.Sms),
            VoiceMinutesRemaining = row.VoiceMinutesRemaining ?? 0,
            SmsRemaining = row.SmsRemaining ?? 0
        };
    }

    private static bool CopyIfDifferent(DailyUsage current, DailyUsage next)
    {
        var changed = false;

        void Set<T>(Func<DailyUsage, T> get, Action<DailyUsage, T> set) where T : IEquatable<T>
        {
            var value = get(next);

            if (!get(current).Equals(value))
            {
                set(current, value);
                changed = true;
            }
        }

        Set(u => u.TotalMb, (u, v) => u.TotalMb = v);
        Set(u => u.TotalDownloadMb, (u, v) => u.TotalDownloadMb = v);
        Set(u => u.TotalUploadMb, (u, v) => u.TotalUploadMb = v);
        Set(u => u.DataSessionCount, (u, v) => u.DataSessionCount = v);
        Set(u => u.DataDurationS, (u, v) => u.DataDurationS = v);
        Set(u => u.HotspotSessions, (u, v) => u.HotspotSessions = v);
        Set(u => u.PublicApnSessions, (u, v) => u.PublicApnSessions = v);
        Set(u => u.UniqueTowers, (u, v) => u.UniqueTowers = v);
        Set(u => u.GeoFlagCount, (u, v) => u.GeoFlagCount = v);
        Set(u => u.PeakMb, (u, v) => u.PeakMb = v);
        Set(u => u.OffpeakMb, (u, v) => u.OffpeakMb = v);
        Set(u => u.RoamingSessions, (u, v) => u.RoamingSessions = v);
        Set(u => u.BilledMb, (u, v) => u.BilledMb = v);
        Set(u => u.Tier, (u, v) => u.Tier = v);
        Set(u => u.VoiceMinutesUsed, (u, v) => u.VoiceMinutesUsed = v);
        Set(u => u.SmsCount, (u, v) => u.SmsCount = v);
        Set(u => u.VoiceMinutesRemaining, (u, v) => u.VoiceMinutesRemaining = v);
        Set(u => u.SmsRemaining, (u, v) => u.SmsRemaining = v);
        return changed;
    }

    // Writes the carrier's status and the import history row.
    private async Task<FeedRunResult> FinishAsync(
        Carrier carrier,
        DateTime startedAt,
        string requestedBy,
        int? userId,
        string status,
        string message,
        int days,
        int fetched,
        int valid,
        int rejected,
        int inserted,
        int updated,
        int unchanged,
        DateOnly? first,
        DateOnly? last)
    {
        var finishedAt = Timestamps.UtcNow();
        var shown = message.Length > MessageMax ? message[..MessageMax] : message;

        carrier.LastSyncStatus = status;
        carrier.LastSyncAt = finishedAt;
        carrier.LastSyncMessage = shown;

        // A run that found nothing new is not worth a history row; a failure always is.
        if (days > 0 || status == FeedStatuses.Failed)
        {
            var range = first is null ? "no days"
                : first == last ? $"{first:yyyy-MM-dd}"
                : $"{first:yyyy-MM-dd} to {last:yyyy-MM-dd}";

            db.ImportRuns.Add(new ImportRun
            {
                Type = ImportTypes.Usage,
                FileName = $"{carrier.Name} feed, {range}",
                Source = ImportSources.CarrierFeed,
                CarrierName = carrier.Name,
                UploadedByUserId = userId,
                UploadedByEmail = requestedBy,
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                Status = status == FeedStatuses.Succeeded ? ImportRunStatuses.Committed : ImportRunStatuses.Failed,
                SkipInvalid = true,
                TotalRows = fetched,
                ValidRows = valid,
                InvalidRows = rejected,
                Inserted = inserted,
                Updated = updated,
                Unchanged = unchanged,
                ErrorCount = status == FeedStatuses.Failed ? 1 : 0,
                WarningCount = 0,
                Message = shown
            });
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return new FeedRunResult(true, status, shown, days, valid, rejected);
    }
}

// One run per carrier at a time, across Sync now and the daily service.
public sealed class CarrierFeedRunner(IServiceScopeFactory scopes, ILogger<CarrierFeedRunner> logger)
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public bool IsRunning(int carrierId) =>
        _locks.TryGetValue(carrierId, out var gate) && gate.CurrentCount == 0;

    // Sync now: starts the run in the background and returns false when one
    // is already running for the carrier.
    public bool TryStartInBackground(int carrierId, string requestedBy, int? userId)
    {
        var gate = _locks.GetOrAdd(carrierId, _ => new SemaphoreSlim(1, 1));

        if (!gate.Wait(0))
        {
            return false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CarrierFeedSync>()
                    .RunAsync(carrierId, requestedBy, userId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Carrier feed run for carrier {CarrierId} crashed.", carrierId);
                await MarkFailedAsync(carrierId);
            }
            finally
            {
                gate.Release();
            }
        });

        return true;
    }

    // Daily service: runs inline; null when a run is already in progress.
    public async Task<FeedRunResult?> RunAsync(int carrierId, string requestedBy, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(carrierId, _ => new SemaphoreSlim(1, 1));

        if (!await gate.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<CarrierFeedSync>()
                .RunAsync(carrierId, requestedBy, null, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Carrier feed run for carrier {CarrierId} crashed.", carrierId);
            await MarkFailedAsync(carrierId);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task MarkFailedAsync(int carrierId)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
            var carrier = await db.Carriers.FirstOrDefaultAsync(c => c.Id == carrierId);

            if (carrier is not null)
            {
                carrier.LastSyncStatus = FeedStatuses.Failed;
                carrier.LastSyncAt = Timestamps.UtcNow();
                carrier.LastSyncMessage = "The run stopped unexpectedly. See the API log.";
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not mark carrier {CarrierId} feed run as failed.", carrierId);
        }
    }
}

// Runs every enabled feed once per CarrierFeed:IntervalMinutes. On start it
// marks runs left "running" by a previous process as failed.
public sealed class CarrierFeedWorker(
    IServiceScopeFactory scopes,
    CarrierFeedRunner runner,
    IOptionsMonitor<CarrierFeedOptions> options,
    ILogger<CarrierFeedWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(options.CurrentValue.StartupDelaySeconds, 0)), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (options.CurrentValue.Enabled)
                {
                    await RunAllAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(Math.Max(options.CurrentValue.IntervalMinutes, 1)), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunAllAsync(CancellationToken stoppingToken)
    {
        List<int> ids;

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SimGuardDbContext>();
            ids = await db.Carriers.AsNoTracking()
                .Where(c => c.FeedEnabled && c.FeedKeySet && c.FeedBaseUrl != null)
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .ToListAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not list carrier feeds to run.");
            return;
        }

        foreach (var id in ids)
        {
            await runner.RunAsync(id, AuditWriter.SystemActor, stoppingToken);
        }
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

public static class CarrierFeedServiceExtensions
{
    public static IServiceCollection AddCarrierFeed(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CarrierFeedOptions>(configuration.GetSection(CarrierFeedOptions.Section));
        services.AddScoped<ICarrierSecretStore, DataProtectionCarrierSecretStore>();
        services.AddScoped<CarrierFeedClient>();
        services.AddScoped<CarrierFeedSync>();
        services.AddSingleton<CarrierFeedRunner>();
        services.AddHostedService<CarrierFeedWorker>();

        // No redirects (the key must go only to the saved address) and no
        // header values in the HTTP client logs.
        services.AddHttpClient(FeedUrl.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan)
            .RedactLoggedHeaders(_ => true);

        var keysPath = configuration["DataProtection:KeysPath"];

        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        return services;
    }
}
