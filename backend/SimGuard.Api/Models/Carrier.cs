namespace SimGuard.Api.Models;

// A mobile network operator. Names are unique ignoring case (enforced by a
// unique index on lower(name)).
public class Carrier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Usage feed (docs/carrier-feed.md). The API key is never on
    // this row: it lives in ICarrierSecretStore; FeedKeySet only says it exists.
    public string? FeedBaseUrl { get; set; }
    public bool FeedEnabled { get; set; }
    public bool FeedKeySet { get; set; }

    // First day to pull when nothing was synced yet.
    public DateOnly? FeedStartDate { get; set; }

    // Last day imported; the next run resumes the day after it.
    public DateOnly? LastSyncDate { get; set; }

    // running, succeeded or failed. Null before the first run.
    public string? LastSyncStatus { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncMessage { get; set; }

    public ICollection<Sim> Sims { get; set; }
        = new List<Sim>();
}
