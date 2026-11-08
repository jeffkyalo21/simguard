using System.Security.Cryptography;
using System.Text;

namespace SimGuard.Api.Ml;

// Request and response shapes of the ML service, as in
// ml-service/contracts/*.json. JSON names are camelCase.

public sealed record MlScoreRequest(IReadOnlyList<int> SimIds, DateOnly AsOfDate);

public sealed record MlReason(string Feature, double Value, double Contribution, string Direction);

public sealed record MlSimScore(
    int SimId,
    DateOnly Date,
    double Probability,
    string Bucket,
    IReadOnlyList<MlReason> Reasons);

public sealed record MlScoreResponse(
    DateOnly AsOfDate,
    IReadOnlyList<MlSimScore> Results,
    IReadOnlyList<int> NotFound);

public sealed record MlRecommendRequest(IReadOnlyList<int> SimIds, int BillingCycle);

public sealed record MlTierResult(
    int SimId,
    int BillingCycle,
    string CurrentTier,
    string Recommendation,
    string? RecommendedTier,
    double? ProjectedOverageMb,
    int? DaysToDepletion,
    int? CheckpointDay,
    string Source);

public sealed record MlFitFailure(int SimId, int CheckpointDay, string Error);

public sealed record MlRecommendTimings(double ReadMs, double FitMs, double TotalMs, int Fitted, int Precomputed);

public sealed record MlRecommendResponse(
    int BillingCycle,
    IReadOnlyList<MlTierResult> Results,
    IReadOnlyList<int> NotFound,
    IReadOnlyList<MlFitFailure> Failed,
    MlRecommendTimings? Timings);

public sealed record MlModelFile(string File, long SizeBytes, string Sha256);

public sealed record MlVersionResponse(
    string Service,
    string ApiVersion,
    IReadOnlyList<MlModelFile> Models,
    IReadOnlyDictionary<string, string> Libraries)
{
    // Stored on risk scores and alerts: the API version plus the first 12
    // hex digits of a SHA-256 over every model file's sha256, in order. Any
    // changed artifact gives a new version. At most 50 characters.
    public string ModelVersion()
    {
        var joined = string.Join("\n", Models.Select(m => $"{m.File}:{m.Sha256}"));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        var version = $"{ApiVersion}-{hash[..12]}";
        return version.Length > 50 ? version[..50] : version;
    }
}

public sealed record MlHealth(bool Reachable, string Status, string? Detail);

// Limits from ml-service/app/config.py (MAX_SCORE_SIMS, MAX_RECOMMEND_SIMS).
public static class MlLimits
{
    public const int MaxScoreSims = 5000;
    public const int MaxRecommendSims = 200;

    // SCORE_LOOKBACK_DAYS: the ML service builds features from the 15 days
    // ending on asOfDate, so a SIM-day is scorable only when all 15 exist.
    public const int ScoreLookbackDays = 15;
}

public enum MlErrorKind
{
    NotConfigured,
    Unreachable,
    Timeout,
    Unauthorized,
    BadRequest,
    Unavailable,
    ServerError,
    InvalidResponse
}

// Every failure of a call to the ML service. Message never contains the
// shared secret.
public sealed class MlServiceException(MlErrorKind kind, string message, int? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public MlErrorKind Kind { get; } = kind;
    public int? StatusCode { get; } = statusCode;
}
