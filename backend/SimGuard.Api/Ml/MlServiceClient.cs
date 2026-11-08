using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SimGuard.Api.Ml;

// Typed client for the ML service (ml-service/app/main.py). Every call sends
// the shared secret header, has its own timeout per attempt and is retried
// once after a transient failure (connection error, timeout, 408, 429, 502,
// 503, 504). Other failures are not retried. Failures are thrown as
// MlServiceException with a message that says what to check.
public sealed class MlServiceClient(
    HttpClient http,
    IOptions<MlServiceOptions> options,
    ILogger<MlServiceClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly MlServiceOptions _options = options.Value;

    public async Task<MlScoreResponse> ScoreAsync(
        IReadOnlyList<int> simIds,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        CheckCount(simIds, MlLimits.MaxScoreSims);
        return await SendAsync<MlScoreResponse>(
            HttpMethod.Post,
            "v1/score",
            new MlScoreRequest(simIds, asOfDate),
            _options.TimeoutSeconds,
            cancellationToken);
    }

    public async Task<MlRecommendResponse> RecommendTierAsync(
        IReadOnlyList<int> simIds,
        int billingCycle,
        CancellationToken cancellationToken)
    {
        CheckCount(simIds, MlLimits.MaxRecommendSims);
        return await SendAsync<MlRecommendResponse>(
            HttpMethod.Post,
            "v1/recommend-tier",
            new MlRecommendRequest(simIds, billingCycle),
            _options.RecommendTimeoutSeconds,
            cancellationToken);
    }

    public Task<MlVersionResponse> GetVersionAsync(CancellationToken cancellationToken) =>
        SendAsync<MlVersionResponse>(HttpMethod.Get, "version", null, _options.HealthTimeoutSeconds, cancellationToken);

    // Never throws for an ML failure: Reachable is false and Detail says why.
    public async Task<MlHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var body = await SendAsync<JsonElement>(
                HttpMethod.Get,
                "health",
                null,
                _options.HealthTimeoutSeconds,
                cancellationToken,
                retry: false);

            var status = body.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
            return new MlHealth(true, status, null);
        }
        catch (MlServiceException ex)
        {
            return new MlHealth(false, ex.Kind == MlErrorKind.Unavailable ? "unavailable" : "unreachable", ex.Message);
        }
    }

    private static void CheckCount(IReadOnlyList<int> simIds, int max)
    {
        if (simIds.Count is 0 || simIds.Count > max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simIds),
                $"Send between 1 and {max} SIM ids per request (got {simIds.Count}).");
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        double timeoutSeconds,
        CancellationToken cancellationToken,
        bool retry = true)
    {
        var baseUri = BaseUri();
        var uri = new Uri(baseUri, path);
        var attempts = retry ? 2 : 1;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendOnceAsync<T>(method, uri, body, timeoutSeconds, cancellationToken);
            }
            catch (MlServiceException ex) when (attempt < attempts && IsTransient(ex))
            {
                logger.LogWarning(
                    "ML service {Method} /{Path} failed ({Kind}), retrying once: {Message}",
                    method,
                    path,
                    ex.Kind,
                    ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), cancellationToken);
            }
        }
    }

    private async Task<T> SendOnceAsync<T>(
        HttpMethod method,
        Uri uri,
        object? body,
        double timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var request = new HttpRequestMessage(method, uri);

        if (!string.IsNullOrEmpty(_options.SharedSecret))
        {
            request.Headers.TryAddWithoutValidation(MlServiceOptions.SecretHeader, _options.SharedSecret);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MlServiceException(
                MlErrorKind.Timeout,
                $"ML service did not answer {method} {uri.AbsolutePath} within {timeoutSeconds:0.#} s.",
                inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MlServiceException(
                MlErrorKind.Unreachable,
                $"ML service at {uri.GetLeftPart(UriPartial.Authority)} is unreachable: {ex.Message}",
                inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadDetailAsync(response, timeout.Token);
                throw Failure(method, uri, response.StatusCode, detail);
            }

            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(Json, timeout.Token);
                return value ?? throw new MlServiceException(
                    MlErrorKind.InvalidResponse,
                    $"ML service returned an empty body for {method} {uri.AbsolutePath}.");
            }
            catch (JsonException ex)
            {
                throw new MlServiceException(
                    MlErrorKind.InvalidResponse,
                    $"ML service returned a body that does not match the contract for {method} {uri.AbsolutePath}: {ex.Message}",
                    (int)response.StatusCode,
                    ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MlServiceException(
                    MlErrorKind.Timeout,
                    $"ML service did not finish the response to {method} {uri.AbsolutePath} within {timeoutSeconds:0.#} s.",
                    inner: ex);
            }
        }
    }

    private Uri BaseUri()
    {
        var value = _options.BaseUrl?.Trim();

        if (string.IsNullOrEmpty(value)
            || !Uri.TryCreate(value.EndsWith('/') ? value : value + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new MlServiceException(
                MlErrorKind.NotConfigured,
                "MlService:BaseUrl is not set to an http or https URL. Set it in user-secrets or the MlService__BaseUrl environment variable.");
        }

        return uri;
    }

    private static bool IsTransient(MlServiceException ex) =>
        ex.Kind is MlErrorKind.Unreachable or MlErrorKind.Timeout
        || ex.StatusCode is 408 or 429 or 502 or 503 or 504;

    private static MlServiceException Failure(HttpMethod method, Uri uri, HttpStatusCode status, string? detail)
    {
        var code = (int)status;
        var where = $"{method} {uri.AbsolutePath}";
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}";

        return code switch
        {
            401 or 403 => new MlServiceException(
                MlErrorKind.Unauthorized,
                $"ML service rejected the shared secret ({code}) on {where}. MlService:SharedSecret must equal ML_SHARED_SECRET.",
                code),
            400 or 404 or 422 => new MlServiceException(
                MlErrorKind.BadRequest,
                $"ML service refused {where} ({code}){suffix}",
                code),
            503 => new MlServiceException(
                MlErrorKind.Unavailable,
                $"ML service is not ready ({code}) on {where}{suffix}",
                code),
            _ => new MlServiceException(
                MlErrorKind.ServerError,
                $"ML service failed on {where} ({code}){suffix}",
                code)
        };
    }

    // The "detail" of the ML problem body, shortened. Never echoes headers.
    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(text);

                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("detail", out var d)
                    && d.ValueKind == JsonValueKind.String)
                {
                    text = d.GetString() ?? text;
                }
            }
            catch (JsonException)
            {
                // Not JSON: use the text as it is.
            }

            return text.Length > 300 ? text[..300] + "..." : text;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public static class MlServiceRegistration
{
    public static IServiceCollection AddMlServiceClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MlServiceOptions>(configuration.GetSection(MlServiceOptions.Section));

        // Timeouts are per attempt inside the client, so the HttpClient's own
        // timeout is off.
        services.AddHttpClient<MlServiceClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);

        return services;
    }
}
