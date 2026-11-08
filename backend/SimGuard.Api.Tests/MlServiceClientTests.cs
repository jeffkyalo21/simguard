using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimGuard.Api.Ml;

namespace SimGuard.Api.Tests;

public sealed class MlServiceClientTests : IAsyncLifetime
{
    private const string Secret = "fake-secret-for-tests";

    private const string ScoreJson = """
        {"asOfDate":"2025-03-15","results":[{"simId":21,"date":"2025-03-15","probability":0.97,"bucket":"flag",
        "reasons":[{"feature":"peak_mb","value":-0.94,"contribution":2.76,"direction":"increases_risk"}],"similarCases":[]}],
        "notFound":[99]}
        """;

    private FakeMlServer _server = null!;

    public async Task InitializeAsync() => _server = await FakeMlServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private MlServiceClient Client(string? baseUrl = null, double timeout = 2, string? secret = Secret) =>
        new(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new MlServiceOptions
            {
                BaseUrl = baseUrl ?? _server.BaseUrl,
                SharedSecret = secret,
                TimeoutSeconds = timeout,
                RecommendTimeoutSeconds = timeout,
                HealthTimeoutSeconds = timeout,
                RetryDelaySeconds = 0.01
            }),
            NullLogger<MlServiceClient>.Instance);

    [Fact]
    public async Task Score_sends_secret_and_contract_body_and_parses_the_response()
    {
        _server.Handler = (ctx, _) => FakeMlServer.Json(ctx, 200, ScoreJson);

        var result = await Client().ScoreAsync([21, 99], new DateOnly(2025, 3, 15), CancellationToken.None);

        var request = Assert.Single(_server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/v1/score", request.Path);
        Assert.Equal(Secret, request.Secret);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("2025-03-15", body.RootElement.GetProperty("asOfDate").GetString());
        Assert.Equal([21, 99], body.RootElement.GetProperty("simIds").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());

        var score = Assert.Single(result.Results);
        Assert.Equal(21, score.SimId);
        Assert.Equal("flag", score.Bucket);
        Assert.Equal("peak_mb", Assert.Single(score.Reasons).Feature);
        Assert.Equal([99], result.NotFound);
    }

    [Fact]
    public async Task Transient_failure_is_retried_once_then_succeeds()
    {
        var calls = 0;
        _server.Handler = (ctx, _) => Interlocked.Increment(ref calls) == 1
            ? FakeMlServer.Json(ctx, 503, """{"title":"Service Unavailable","status":503,"detail":"warming up"}""")
            : FakeMlServer.Json(ctx, 200, ScoreJson);

        var result = await Client().ScoreAsync([21], new DateOnly(2025, 3, 15), CancellationToken.None);

        Assert.Equal(2, _server.Requests.Count);
        Assert.Single(result.Results);
    }

    [Fact]
    public async Task Two_transient_failures_give_a_clear_error_after_exactly_two_attempts()
    {
        _server.Handler = (ctx, _) =>
            FakeMlServer.Json(ctx, 503, """{"title":"Service Unavailable","status":503,"detail":"Model artifacts missing: x"}""");

        var ex = await Assert.ThrowsAsync<MlServiceException>(
            () => Client().ScoreAsync([21], new DateOnly(2025, 3, 15), CancellationToken.None));

        Assert.Equal(MlErrorKind.Unavailable, ex.Kind);
        Assert.Equal(503, ex.StatusCode);
        Assert.Contains("Model artifacts missing", ex.Message);
        Assert.Equal(2, _server.Requests.Count);
    }

    [Fact]
    public async Task Rejected_secret_is_not_retried_and_never_echoes_the_secret()
    {
        _server.Handler = (ctx, _) =>
            FakeMlServer.Json(ctx, 401, """{"title":"Unauthorized","status":401,"detail":"Missing or wrong header."}""");

        var ex = await Assert.ThrowsAsync<MlServiceException>(
            () => Client().GetVersionAsync(CancellationToken.None));

        Assert.Equal(MlErrorKind.Unauthorized, ex.Kind);
        Assert.Contains("MlService:SharedSecret", ex.Message);
        Assert.DoesNotContain(Secret, ex.Message);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task Validation_error_is_not_retried_and_carries_the_detail()
    {
        _server.Handler = (ctx, _) =>
            FakeMlServer.Json(ctx, 422, """{"title":"Unprocessable Entity","status":422,"detail":"asOfDate: bad"}""");

        var ex = await Assert.ThrowsAsync<MlServiceException>(
            () => Client().ScoreAsync([21], new DateOnly(2025, 3, 15), CancellationToken.None));

        Assert.Equal(MlErrorKind.BadRequest, ex.Kind);
        Assert.Contains("asOfDate: bad", ex.Message);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task Slow_answer_times_out_and_is_retried_once()
    {
        _server.Handler = async (ctx, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            await FakeMlServer.Json(ctx, 200, ScoreJson);
        };

        var ex = await Assert.ThrowsAsync<MlServiceException>(
            () => Client(timeout: 0.3).ScoreAsync([21], new DateOnly(2025, 3, 15), CancellationToken.None));

        Assert.Equal(MlErrorKind.Timeout, ex.Kind);
        Assert.Contains("within", ex.Message);
        Assert.Equal(2, _server.Requests.Count);
    }

    [Fact]
    public async Task Server_that_is_down_is_reported_as_unreachable()
    {
        var closed = $"http://127.0.0.1:{FakeMlServer.FreePort()}";

        var ex = await Assert.ThrowsAsync<MlServiceException>(
            () => Client(closed, timeout: 15).ScoreAsync([21], new DateOnly(2025, 3, 15), CancellationToken.None));

        Assert.Equal(MlErrorKind.Unreachable, ex.Kind);
        Assert.Contains("unreachable", ex.Message);

        var health = await Client(closed, timeout: 15).GetHealthAsync(CancellationToken.None);
        Assert.False(health.Reachable);
        Assert.Equal("unreachable", health.Status);
    }

    [Fact]
    public async Task Health_is_reachable_and_sends_the_secret()
    {
        _server.Handler = (ctx, _) => FakeMlServer.Json(
            ctx, 200, """{"status":"ok","artifactsLoaded":true,"missingArtifacts":[],"reader":"postgres"}""");

        var health = await Client().GetHealthAsync(CancellationToken.None);

        Assert.True(health.Reachable);
        Assert.Equal("ok", health.Status);
        Assert.Equal("/health", Assert.Single(_server.Requests).Path);
    }

    [Fact]
    public async Task Health_503_is_reachable_false_with_status_unavailable()
    {
        _server.Handler = (ctx, _) => FakeMlServer.Json(
            ctx, 503, """{"status":"unavailable","artifactsLoaded":false,"missingArtifacts":["x"],"reader":"postgres"}""");

        var health = await Client().GetHealthAsync(CancellationToken.None);

        Assert.False(health.Reachable);
        Assert.Equal("unavailable", health.Status);
    }

    [Fact]
    public async Task Version_gives_a_stable_model_version()
    {
        const string json = """
            {"service":"simguard-ml","apiVersion":"v1","models":[{"file":"a.joblib","sizeBytes":1,"sha256":"aa"},
            {"file":"b.json","sizeBytes":2,"sha256":"bb"}],"libraries":{"python":"3.13"}}
            """;
        _server.Handler = (ctx, _) => FakeMlServer.Json(ctx, 200, json);

        var first = await Client().GetVersionAsync(CancellationToken.None);
        var second = await Client().GetVersionAsync(CancellationToken.None);

        Assert.StartsWith("v1-", first.ModelVersion());
        Assert.Equal(15, first.ModelVersion().Length);
        Assert.Equal(first.ModelVersion(), second.ModelVersion());

        var changed = first with { Models = [first.Models[0], first.Models[1] with { Sha256 = "cc" }] };
        Assert.NotEqual(first.ModelVersion(), changed.ModelVersion());
    }

    [Fact]
    public async Task Recommend_sends_contract_body()
    {
        _server.Handler = (ctx, _) => FakeMlServer.Json(ctx, 200, """
            {"billingCycle":2,"results":[{"simId":187,"billingCycle":2,"currentTier":"professional","recommendation":"upgrade",
            "recommendedTier":"manager","projectedOverageMb":924.3,"daysToDepletion":5,"checkpointDay":25,"source":"fitted"}],
            "notFound":[],"failed":[],"timings":{"readMs":1,"fitMs":2,"totalMs":3,"fitted":1,"precomputed":0}}
            """);

        var result = await Client().RecommendTierAsync([187], 2, CancellationToken.None);

        var request = Assert.Single(_server.Requests);
        Assert.Equal("/v1/recommend-tier", request.Path);
        Assert.Equal("""{"simIds":[187],"billingCycle":2}""", request.Body);
        Assert.Equal("manager", Assert.Single(result.Results).RecommendedTier);
    }

    [Fact]
    public async Task Contract_limits_are_checked_before_sending()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Client().ScoreAsync([], new DateOnly(2025, 3, 15), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Client().ScoreAsync(Enumerable.Range(1, MlLimits.MaxScoreSims + 1).ToList(), new DateOnly(2025, 3, 15), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Client().RecommendTierAsync(Enumerable.Range(1, MlLimits.MaxRecommendSims + 1).ToList(), 1, CancellationToken.None));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task Missing_base_url_is_a_configuration_error()
    {
        var client = new MlServiceClient(
            new HttpClient(),
            Options.Create(new MlServiceOptions()),
            NullLogger<MlServiceClient>.Instance);

        var ex = await Assert.ThrowsAsync<MlServiceException>(() => client.GetVersionAsync(CancellationToken.None));
        Assert.Equal(MlErrorKind.NotConfigured, ex.Kind);
        Assert.Contains("MlService:BaseUrl", ex.Message);
    }

    [Fact]
    public async Task Body_that_breaks_the_contract_is_an_invalid_response()
    {
        _server.Handler = (ctx, _) => FakeMlServer.Json(ctx, 200, """{"asOfDate":"not a date","results":5}""");

        var ex = await Assert.ThrowsAsync<MlServiceException>(
            () => Client().ScoreAsync([21], new DateOnly(2025, 3, 15), CancellationToken.None));
        Assert.Equal(MlErrorKind.InvalidResponse, ex.Kind);
    }
}
