using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Feed;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

// Carrier usage feed settings. Contract in docs/carrier-feed.md.
// The API key is write-only: no response, audit row or log contains it.
public static class CarrierFeedEndpoints
{
    private const int KeyMaxLength = 500;

    public static IEndpointRouteBuilder MapCarrierFeedEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/carriers/{id:int}/feed").WithTags("Carriers");

        group.MapPut("/", SaveFeed)
            .WithName("SaveCarrierFeed")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<CarrierResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/test", TestFeed)
            .WithName("TestCarrierFeed")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<CarrierFeedTestResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/sync", SyncFeed)
            .WithName("SyncCarrierFeed")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<CarrierResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> SaveFeed(
        int id,
        CarrierFeedRequest request,
        SimGuardDbContext db,
        ICarrierSecretStore secrets,
        AuditWriter audit,
        IOptions<CarrierFeedOptions> options,
        CancellationToken cancellationToken)
    {
        var carrier = await db.Carriers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (carrier is null)
        {
            return ApiProblems.NotFound($"Carrier {id} was not found.");
        }

        var errors = new ValidationErrors();
        var enabled = request.Enabled ?? false;
        var newKey = request.ApiKey?.Trim();
        string? baseUrl = null;

        if (!string.IsNullOrWhiteSpace(request.BaseUrl) || enabled)
        {
            baseUrl = FeedUrl.Validate(request.BaseUrl, options.Value.AllowInsecureHttp, out var urlError);

            if (urlError is not null)
            {
                errors.Add("baseUrl", urlError);
            }
        }

        DateOnly? startDate = null;

        if (!string.IsNullOrWhiteSpace(request.StartDate))
        {
            if (!DateOnly.TryParseExact(request.StartDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                errors.Add("startDate", "startDate must be a date like 2025-01-31.");
            }
            else
            {
                startDate = parsed;
            }
        }

        if (!string.IsNullOrEmpty(newKey) && (newKey.Length > KeyMaxLength || newKey.Any(ch => ch < 0x21 || ch > 0x7E)))
        {
            errors.Add("apiKey", $"The API key must be up to {KeyMaxLength} visible characters with no spaces.");
        }

        if (enabled && string.IsNullOrEmpty(newKey) && !carrier.FeedKeySet)
        {
            errors.Add("apiKey", "Enter an API key before turning the feed on.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var before = Snapshot(carrier, keyChanged: false);
        var keyChanged = !string.IsNullOrEmpty(newKey);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Choosing a different start date at or before the last synced day
        // makes the next sync pull again from that day (a backfill or re-pull;
        // rows are upserted, so nothing is duplicated). Before the first sync
        // the start date alone decides where to begin.
        if (startDate is not null && startDate != carrier.FeedStartDate
            && carrier.LastSyncDate is not null && startDate <= carrier.LastSyncDate)
        {
            carrier.LastSyncDate = startDate.Value.AddDays(-1);
        }

        carrier.FeedBaseUrl = baseUrl;
        carrier.FeedEnabled = enabled;
        carrier.FeedStartDate = startDate;

        if (keyChanged)
        {
            carrier.FeedKeySet = true;
            await secrets.SetAsync(carrier.Id, newKey!, cancellationToken);
        }

        audit.Add(AuditActions.CarrierFeedSaved, null, before, Snapshot(carrier, keyChanged));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var simCount = await db.Sims.CountAsync(s => s.CarrierId == id, cancellationToken);
        return TypedResults.Ok(CarrierResponse.From(carrier, simCount));
    }

    private static async Task<IResult> TestFeed(
        int id,
        CarrierFeedTestRequest? request,
        SimGuardDbContext db,
        ICarrierSecretStore secrets,
        CarrierFeedClient client,
        AuditWriter audit,
        IOptions<CarrierFeedOptions> options,
        CancellationToken cancellationToken)
    {
        var carrier = await db.Carriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (carrier is null)
        {
            return ApiProblems.NotFound($"Carrier {id} was not found.");
        }

        var errors = new ValidationErrors();
        var baseUrl = FeedUrl.Validate(
            string.IsNullOrWhiteSpace(request?.BaseUrl) ? carrier.FeedBaseUrl : request.BaseUrl,
            options.Value.AllowInsecureHttp,
            out var urlError);

        if (urlError is not null)
        {
            errors.Add("baseUrl", urlError);
        }

        var key = string.IsNullOrWhiteSpace(request?.ApiKey)
            ? await secrets.GetAsync(id, cancellationToken)
            : request.ApiKey.Trim();

        if (string.IsNullOrEmpty(key))
        {
            errors.Add("apiKey", "Enter an API key to test.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        // The last synced day, so the test reads data the feed has certainly published.
        var date = carrier.LastSyncDate
            ?? carrier.FeedStartDate
            ?? DateOnly.FromDateTime(Timestamps.UtcNow()).AddDays(-1);

        bool ok;
        string message;

        try
        {
            var page = await client.GetSummaryPageAsync(baseUrl!, key!, date, 1, 1, false, cancellationToken);
            ok = true;
            message = $"Connected. The carrier returned {page.Total} row(s) for {date:yyyy-MM-dd}.";
        }
        catch (FeedDateNotAvailableException)
        {
            ok = true;
            message = $"Connected. The carrier has no data for {date:yyyy-MM-dd} yet.";
        }
        catch (FeedException ex)
        {
            ok = false;
            message = ex.Message;
        }

        await audit.WriteAsync(
            AuditActions.CarrierFeedTested, null, null,
            new { carrierId = id, carrier = carrier.Name, ok, date = date.ToString("yyyy-MM-dd") },
            cancellationToken);

        return TypedResults.Ok(new CarrierFeedTestResponse(ok, message, date.ToString("yyyy-MM-dd")));
    }

    private static async Task<IResult> SyncFeed(
        int id,
        System.Security.Claims.ClaimsPrincipal user,
        SimGuardDbContext db,
        CarrierFeedRunner runner,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var carrier = await db.Carriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (carrier is null)
        {
            return ApiProblems.NotFound($"Carrier {id} was not found.");
        }

        if (string.IsNullOrEmpty(carrier.FeedBaseUrl) || !carrier.FeedKeySet)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "Save a feed address and an API key before syncing.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (runner.IsRunning(id))
        {
            return ApiProblems.Conflict($"A sync is already running for carrier '{carrier.Name}'.");
        }

        var userId = TokenService.GetUserId(user);
        var email = user.FindFirst(TokenService.EmailClaim)?.Value ?? "unknown";

        await audit.WriteAsync(
            AuditActions.CarrierFeedSyncRequested, null, null,
            new { carrierId = id, carrier = carrier.Name },
            cancellationToken);

        if (!runner.TryStartInBackground(id, email, userId))
        {
            return ApiProblems.Conflict($"A sync is already running for carrier '{carrier.Name}'.");
        }

        var simCount = await db.Sims.CountAsync(s => s.CarrierId == id, cancellationToken);
        return TypedResults.Accepted(
            $"/api/carriers",
            CarrierResponse.From(carrier, simCount) with { LastSyncStatus = FeedStatuses.Running });
    }

    // What the audit log records: never the key, only whether one is saved.
    private static object Snapshot(Carrier carrier, bool keyChanged) => new
    {
        carrierId = carrier.Id,
        carrier = carrier.Name,
        baseUrl = carrier.FeedBaseUrl,
        enabled = carrier.FeedEnabled,
        startDate = carrier.FeedStartDate?.ToString("yyyy-MM-dd"),
        lastSyncDate = carrier.LastSyncDate?.ToString("yyyy-MM-dd"),
        keySet = carrier.FeedKeySet,
        keyChanged
    };
}
