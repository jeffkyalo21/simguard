using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Models;
using SimGuard.Api.TierRecommendations;

namespace SimGuard.Api.Endpoints;

public record TierRecommendationSettingsResponse(
    int IntervalDays,
    int MinIntervalDays,
    int MaxIntervalDays,
    string? UpdatedBy,
    DateTime? UpdatedAt);

public record TierRecommendationSettingsRequest(int? IntervalDays);

public record TierRecommendationRunResponse(
    int Id,
    string Trigger,
    string Status,
    string RequestedBy,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int? BillingCycle,
    int SimsEvaluated,
    int RecommendationsCreated,
    string? ErrorSummary);

public record TierRecommendationRunStarted(int RunId);

public record TierRecommendationStatusResponse(
    TierRecommendationRunResponse? LastRun,
    int IntervalDays,
    int AutoRunGraceDays,
    DateOnly? NextAllowedRunDate,
    bool AutomaticRunDue,
    bool Running);

public record TierRecommendationItem(
    int Id,
    int SimId,
    string SimPhoneNumber,
    string CurrentTier,
    string RecommendedTier,
    string Recommendation,
    string Reason,
    int BillingCycle,
    double? ProjectedOverageMb,
    int? DaysToDepletion,
    string Status,
    DateTime CreatedAt,
    int RunId,
    DateTime? DismissedAt);

// Tier recommendations (issue #14). Contract in
// backend/docs/api-tier-recommendations.md. Reading is OrgRead
// (SecurityOfficer, Auditor); changes are OrgWrite (SecurityOfficer).
public static class TierRecommendationEndpoints
{
    public static IEndpointRouteBuilder MapTierRecommendationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tier-recommendations")
            .WithTags("Tier recommendations")
            .RequireAuthorization(AuthPolicies.OrgRead);

        group.MapGet("/", List)
            .WithName("GetTierRecommendations")
            .Produces<PagedResult<TierRecommendationItem>>()
            .ProducesValidationProblem();

        group.MapGet("/settings", GetSettings)
            .WithName("GetTierRecommendationSettings")
            .Produces<TierRecommendationSettingsResponse>();

        group.MapPut("/settings", PutSettings)
            .WithName("UpdateTierRecommendationSettings")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<TierRecommendationSettingsResponse>()
            .ProducesValidationProblem();

        group.MapGet("/status", GetStatus)
            .WithName("GetTierRecommendationStatus")
            .Produces<TierRecommendationStatusResponse>();

        group.MapPost("/run", Run)
            .WithName("RunTierRecommendations")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<TierRecommendationRunStarted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:int}/dismiss", Dismiss)
            .WithName("DismissTierRecommendation")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<TierRecommendationItem>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> List(
        string? status,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);

        if (status is not null && !TierRecommendationStatuses.All.Contains(status))
        {
            errors.Add("status", "status must be open or dismissed.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var query = db.TierRecommendations.AsNoTracking();

        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        var result = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(Item)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetSettings(SimGuardDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(Settings(await LoadSettingsAsync(db, cancellationToken)));

    private static async Task<IResult> PutSettings(
        TierRecommendationSettingsRequest request,
        HttpContext http,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();

        if (request.IntervalDays is not { } days)
        {
            errors.Add("intervalDays", "intervalDays is required.");
        }
        else if (days is < TierRecommendationOptions.MinIntervalDays or > TierRecommendationOptions.MaxIntervalDays)
        {
            errors.Add("intervalDays",
                $"intervalDays must be between {TierRecommendationOptions.MinIntervalDays} and {TierRecommendationOptions.MaxIntervalDays}.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var settings = await LoadSettingsAsync(db, cancellationToken);
        var old = settings.IntervalDays;

        settings.IntervalDays = request.IntervalDays!.Value;
        settings.UpdatedByUserId = TokenService.GetUserId(http.User);
        settings.UpdatedBy = http.User.FindFirst(TokenService.EmailClaim)?.Value;
        settings.UpdatedAt = Timestamps.UtcNow();

        audit.Add(AuditActions.TierRecommendationSettingsChanged, null,
            new { intervalDays = old }, new { intervalDays = settings.IntervalDays });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(Settings(settings));
    }

    private static async Task<IResult> GetStatus(
        SimGuardDbContext db,
        TierRecommendationService service,
        CancellationToken cancellationToken)
    {
        var s = await service.GetScheduleAsync(db, Timestamps.UtcNow(), cancellationToken);

        return TypedResults.Ok(new TierRecommendationStatusResponse(
            s.LastRun is null ? null : RunResponse(s.LastRun),
            s.IntervalDays,
            s.AutoRunGraceDays,
            s.NextAllowedAt is { } next ? DateOnly.FromDateTime(next) : null,
            s.AutomaticRunDue,
            s.Running));
    }

    // ?force=true skips the interval check in Development only; elsewhere
    // the parameter is ignored.
    private static async Task<IResult> Run(
        bool? force,
        HttpContext http,
        IWebHostEnvironment environment,
        TierRecommendationService service,
        CancellationToken cancellationToken)
    {
        var skipInterval = force == true && environment.IsDevelopment();

        var start = await service.TryStartAsync(
            TierRunTriggers.Manual,
            TokenService.GetUserId(http.User),
            http.User.FindFirst(TokenService.EmailClaim)?.Value ?? "unknown",
            skipInterval,
            cancellationToken);

        if (start.RunId is { } runId)
        {
            return TypedResults.Accepted($"/api/tier-recommendations/status", new TierRecommendationRunStarted(runId));
        }

        return ApiProblems.Conflict(start.Conflict!, new Dictionary<string, object?>
        {
            ["nextAllowedRunDate"] = start.NextAllowedAt is { } next ? DateOnly.FromDateTime(next).ToString("yyyy-MM-dd") : null
        });
    }

    private static async Task<IResult> Dismiss(
        int id,
        HttpContext http,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var rec = await db.TierRecommendations.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rec is null)
        {
            return ApiProblems.NotFound($"Tier recommendation {id} was not found.");
        }

        if (rec.Status != TierRecommendationStatuses.Open)
        {
            return ApiProblems.Conflict($"Tier recommendation {id} is already {rec.Status}.");
        }

        rec.Status = TierRecommendationStatuses.Dismissed;
        rec.DismissedAt = Timestamps.UtcNow();
        rec.DismissedByUserId = TokenService.GetUserId(http.User);

        audit.Add(AuditActions.TierRecommendationDismissed, rec.SimId,
            new { recommendationId = rec.Id, status = TierRecommendationStatuses.Open },
            new { recommendationId = rec.Id, status = rec.Status, rec.RecommendedTier });
        await db.SaveChangesAsync(cancellationToken);

        var item = await db.TierRecommendations.AsNoTracking().Where(r => r.Id == id).Select(Item).SingleAsync(cancellationToken);
        return TypedResults.Ok(item);
    }

    private static async Task<TierRecommendationSettings> LoadSettingsAsync(SimGuardDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.TierRecommendationSettings.SingleOrDefaultAsync(s => s.Id == 1, cancellationToken);

        if (settings is null)
        {
            settings = new TierRecommendationSettings { Id = 1, IntervalDays = TierRecommendationOptions.DefaultIntervalDays };
            db.TierRecommendationSettings.Add(settings);
        }

        return settings;
    }

    private static TierRecommendationSettingsResponse Settings(TierRecommendationSettings s) =>
        new(s.IntervalDays, TierRecommendationOptions.MinIntervalDays, TierRecommendationOptions.MaxIntervalDays,
            s.UpdatedBy, s.UpdatedAt);

    internal static TierRecommendationRunResponse RunResponse(TierRecommendationRun r) =>
        new(r.Id, r.Trigger, r.Status, r.RequestedBy, r.StartedAt, r.FinishedAt, r.BillingCycle,
            r.SimsEvaluated, r.RecommendationsCreated, r.ErrorSummary);

    private static readonly System.Linq.Expressions.Expression<Func<TierRecommendation, TierRecommendationItem>> Item =
        r => new TierRecommendationItem(
            r.Id, r.SimId, r.Sim.PhoneNumber, r.CurrentTier, r.RecommendedTier, r.Recommendation, r.Reason,
            r.BillingCycle, r.ProjectedOverageMb, r.DaysToDepletion, r.Status, r.CreatedAt, r.RunId, r.DismissedAt);
}
