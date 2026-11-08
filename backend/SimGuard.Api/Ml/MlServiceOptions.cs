namespace SimGuard.Api.Ml;

// Configuration section "MlService". BaseUrl and SharedSecret come from
// user-secrets or environment variables (MlService__BaseUrl,
// MlService__SharedSecret) and are never committed.
public sealed class MlServiceOptions
{
    public const string Section = "MlService";

    // Header that carries the shared secret; the ML service compares it with
    // ML_SHARED_SECRET on every route except /health.
    public const string SecretHeader = "X-SimGuard-ML-Secret";

    public string? BaseUrl { get; set; }

    public string? SharedSecret { get; set; }

    // Per attempt. A score batch of up to 5000 SIMs takes a few seconds.
    public double TimeoutSeconds { get; set; } = 120;

    // Per attempt. Prophet fits up to 200 SIMs per request.
    public double RecommendTimeoutSeconds { get; set; } = 600;

    // Per attempt, for /health and /version.
    public double HealthTimeoutSeconds { get; set; } = 5;

    // Pause before the single retry after a transient failure.
    public double RetryDelaySeconds { get; set; } = 1;
}
