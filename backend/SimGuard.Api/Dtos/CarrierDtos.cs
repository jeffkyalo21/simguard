using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

// Used by POST and PUT.
public record CarrierRequest(string? Name);

// The feed fields are additive. The API key is never returned, only
// whether one is saved (feedKeySet).
public record CarrierResponse(
    int Id,
    string Name,
    DateTime CreatedAt,
    int SimCount,
    string? FeedBaseUrl = null,
    bool FeedEnabled = false,
    bool FeedKeySet = false,
    DateOnly? FeedStartDate = null,
    DateOnly? LastSyncDate = null,
    string? LastSyncStatus = null,
    DateTime? LastSyncAt = null,
    string? LastSyncMessage = null)
{
    public static readonly Expression<Func<Carrier, CarrierResponse>> Projection =
        c => new CarrierResponse(
            c.Id, c.Name, c.CreatedAt, c.Sims.Count(),
            c.FeedBaseUrl, c.FeedEnabled, c.FeedKeySet, c.FeedStartDate,
            c.LastSyncDate, c.LastSyncStatus, c.LastSyncAt, c.LastSyncMessage);

    public static CarrierResponse From(Carrier c, int simCount) =>
        new(c.Id, c.Name, c.CreatedAt, simCount,
            c.FeedBaseUrl, c.FeedEnabled, c.FeedKeySet, c.FeedStartDate,
            c.LastSyncDate, c.LastSyncStatus, c.LastSyncAt, c.LastSyncMessage);
}

// PUT /api/carriers/{id}/feed. ApiKey is optional: leave it out to keep the
// saved key. It is never returned by any endpoint.
public record CarrierFeedRequest(string? BaseUrl, bool? Enabled, string? StartDate, string? ApiKey);

// POST /api/carriers/{id}/feed/test. Both fields are optional: a missing one
// is taken from the saved settings, so unsaved values can be tested.
public record CarrierFeedTestRequest(string? BaseUrl, string? ApiKey);

public record CarrierFeedTestResponse(bool Ok, string Message, string Date);
