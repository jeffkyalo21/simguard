using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SimGuard.Api.Feed;

public sealed class CarrierFeedOptions
{
    public const string Section = "CarrierFeed";

    // Runs the daily background service.
    public bool Enabled { get; set; } = true;

    // Local time (HH:mm) of the daily run of every enabled feed, in TimeZone.
    // Before the 02:00 scoring run (Scoring:DailyRunTimeNairobi), so a day the
    // carrier publishes overnight is imported first.
    public string DailySyncTime { get; set; } = "01:30";

    // IANA time zone of DailySyncTime; the server's own clock zone is not used.
    public string TimeZone { get; set; } = "Africa/Nairobi";

    // Wait after the API starts before the first check (and its catch-up run).
    public int StartupDelaySeconds { get; set; } = 60;

    public int RequestTimeoutSeconds { get; set; } = 30;

    // Tries per request for 429, 5xx and timeouts.
    public int MaxAttempts { get; set; } = 4;
    public int RetryBaseDelayMs { get; set; } = 2000;
    public int MaxRetryDelaySeconds { get; set; } = 30;

    // Days pulled by one run; the next run continues.
    public int MaxDaysPerRun { get; set; } = 31;

    public int PageSize { get; set; } = 500;

    // First sync of a feed with no start date and no history.
    public int DefaultLookbackDays { get; set; } = 7;

    // Plain http is accepted only for loopback addresses unless this is true.
    public bool AllowInsecureHttp { get; set; }
}

// One row of GET /v1/usage/summary. Every field except iccid is optional in
// the contract; the importer rejects a row that lacks what it needs.
public sealed record FeedRow(
    string? Iccid,
    string? Date,
    double? DataMb,
    double? VoiceMinutes,
    long? Sms,
    double? RoamingMb,
    double? BilledMb,
    double? DownloadMb,
    double? UploadMb,
    long? DataSessionCount,
    long? DataDurationS,
    long? HotspotSessions,
    long? PublicApnSessions,
    long? UniqueTowers,
    long? GeoFlagCount,
    double? PeakMb,
    double? OffpeakMb,
    long? RoamingSessions,
    string? Tier,
    double? VoiceMinutesRemaining,
    long? SmsRemaining);

public sealed record FeedPage(int Total, IReadOnlyList<FeedRow> Rows);

// A failure with a message fit to show a person. Never contains the key.
public sealed class FeedException(string message) : Exception(message);

// The carrier has not published this date yet (404 date_not_available).
public sealed class FeedDateNotAvailableException(DateOnly date)
    : Exception($"The carrier has no data for {date:yyyy-MM-dd} yet.");

public static class FeedUrl
{
    public const string HttpClientName = "carrier-feed";

    // Returns the normalised base URL (no trailing slash) or an error.
    public static string? Validate(string? value, bool allowInsecureHttp, out string? error)
    {
        error = null;
        var text = value?.Trim();

        if (string.IsNullOrEmpty(text) || text.Length > 300
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "Enter a web address that starts with https:// (up to 300 characters, no query string).";
            return null;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !allowInsecureHttp && !uri.IsLoopback)
        {
            error = "Use an https:// address. Plain http is only allowed for localhost.";
            return null;
        }

        return text.TrimEnd('/');
    }
}

// Calls a carrier's usage feed: GET {base}/v1/usage/summary?date=&page=&pageSize=
// with the X-Api-Key header. Retries 429, 5xx and timeouts with backoff,
// fails at once on 401. The key is only ever put in the request header.
public sealed class CarrierFeedClient(
    IHttpClientFactory httpClients,
    IOptionsMonitor<CarrierFeedOptions> options,
    ILogger<CarrierFeedClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<FeedPage> GetSummaryPageAsync(
        string baseUrl,
        string apiKey,
        DateOnly date,
        int page,
        int pageSize,
        bool retry,
        CancellationToken cancellationToken)
    {
        var opts = options.CurrentValue;
        var attempts = retry ? Math.Max(opts.MaxAttempts, 1) : 1;
        var url = $"{baseUrl}/v1/usage/summary?date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&page={page}&pageSize={pageSize}";

        for (var attempt = 1; ; attempt++)
        {
            FeedException failure;
            TimeSpan? retryAfter = null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
                request.Headers.Accept.ParseAdd("application/json");

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(opts.RequestTimeoutSeconds, 1)));

                using var response = await httpClients.CreateClient(FeedUrl.HttpClientName)
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);

                var status = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var text = await response.Content.ReadAsStringAsync(timeout.Token);
                    return Parse(text);
                }

                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        throw new FeedException("The carrier rejected the API key (401). Check the key and save it again.");
                    case HttpStatusCode.Forbidden:
                        throw new FeedException("The carrier refused access (403). Check that the key is allowed to read usage.");
                    case HttpStatusCode.NotFound:
                        if (await IsDateNotAvailableAsync(response, timeout.Token))
                        {
                            throw new FeedDateNotAvailableException(date);
                        }

                        throw new FeedException("The feed address was not found (404). Check the URL.");
                    case HttpStatusCode.TooManyRequests:
                        retryAfter = ReadRetryAfter(response);
                        failure = new FeedException("The carrier is rate limiting requests (429). Try again later.");
                        break;
                    default:
                        if (status >= 500)
                        {
                            failure = new FeedException($"The carrier feed had a server error (HTTP {status}). Try again later.");
                            break;
                        }

                        throw new FeedException($"The carrier feed refused the request (HTTP {status}).");
                }
            }
            catch (FeedException)
            {
                throw;
            }
            catch (FeedDateNotAvailableException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                failure = new FeedException("The carrier feed did not answer in time.");
            }
            catch (HttpRequestException)
            {
                failure = new FeedException("Could not reach the carrier feed. Check the URL.");
            }

            if (attempt >= attempts)
            {
                throw failure;
            }

            var delay = retryAfter ?? TimeSpan.FromMilliseconds(opts.RetryBaseDelayMs * Math.Pow(2, attempt - 1));
            var max = TimeSpan.FromSeconds(Math.Max(opts.MaxRetryDelaySeconds, 1));
            delay = delay > max ? max : delay;

            logger.LogWarning("Carrier feed request failed (attempt {Attempt} of {Attempts}); retrying in {Delay}.",
                attempt, attempts, delay);
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static FeedPage Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var items = root.GetProperty("items").Deserialize<List<FeedRow>>(Json) ?? [];
            var total = root.TryGetProperty("total", out var t) && t.TryGetInt32(out var n) ? n : items.Count;
            return new FeedPage(total, items);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FeedException("The carrier feed returned a response SimGuard could not read.");
        }
    }

    private static async Task<bool> IsDateNotAvailableAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return doc.RootElement.TryGetProperty("code", out var code) && code.GetString() == "date_not_available";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;

        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
