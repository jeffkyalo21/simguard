
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Email;
using SimGuard.Api.Endpoints;
using SimGuard.Api.Feed;
using SimGuard.Api.Ml;
using SimGuard.Api.Scoring;
using SimGuard.Api.TierRecommendations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SimGuardDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();

// Typed client for the ML service. See backend/docs/scoring-pipeline.md.
builder.Services.AddMlServiceClient(builder.Configuration);

// Scoring runs: queue, runner and the background worker.
builder.Services.AddScoringPipeline(builder.Configuration);
builder.Services.AddTierRecommendations(builder.Configuration);

// Carrier usage feeds: encrypted keys, importer and the daily service.
// See docs/carrier-feed.md.
builder.Services.AddCarrierFeed(builder.Configuration);

// Transactional email through Brevo. See backend/docs/access-requests.md.
builder.Services.AddEmail(builder.Configuration);

// Identity, JWT bearer, authorization policies, CORS, rate limiting and
// Swagger with a Bearer scheme. See backend/docs/auth.md.
builder.AddSimGuardSecurity();

// The Next.js server calls this API for every visitor, so the client address
// (used by the rate limits) comes from X-Forwarded-For. It is trusted only
// from loopback and the proxies listed in ForwardedHeaders:KnownProxies.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;

    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }
});

var app = builder.Build();

if (app.Services.GetRequiredService<JwtSettings>().UsesEphemeralKey)
{
    app.Logger.LogWarning(
        "Jwt:Key is not set. Using a random signing key; tokens stop working when the API restarts.");
}

await IdentitySeeder.SeedAsync(app.Services);

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(SecurityServiceExtensions.CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new
{
    name = "SimGuard API",
    status = "Running"
}))
.WithName("GetApiStatus")
.AllowAnonymous();

// For hosting probes: anonymous, checks only the database, says nothing
// about the ML service or the data.
app.MapGet("/health", async (
    SimGuardDbContext db,
    CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "healthy" })
        : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable))
.WithName("GetHealth")
.AllowAnonymous();

app.MapGet("/health/database", async (
    SimGuardDbContext db,
    CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);

    // Requires a signed-in user (fallback policy) and does not name the
    // database engine.
    return canConnect
        ? Results.Ok(new { status = "connected" })
        : Results.Problem("Database connection failed.", statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("CheckDatabaseHealth")
.RequireAuthorization(AuthPolicies.AnyRole);

var api = app.MapGroup("/api");

api.MapDepartmentEndpoints();
api.MapEmployeeEndpoints();
api.MapCarrierEndpoints();
api.MapCarrierFeedEndpoints();
api.MapSimEndpoints();
api.MapAllocationEndpoints();
api.MapAuthEndpoints();
api.MapUserEndpoints();
api.MapAlertEndpoints();
api.MapRiskEndpoints();
api.MapUsageEndpoints();
api.MapDashboardEndpoints();
api.MapAuditLogEndpoints();
api.MapImportEndpoints();
api.MapAccessRequestEndpoints();
api.MapTierRecommendationEndpoints();
api.MapAdminScoringEndpoints();

// Demo data tools exist only in Development; elsewhere the routes are not
// mapped at all, so they return 404.
if (app.Environment.IsDevelopment())
{
    api.MapDevEndpoints();
}

app.Run();
