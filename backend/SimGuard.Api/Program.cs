
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SimGuardDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();

// Identity, JWT bearer, authorization policies, CORS, rate limiting and
// Swagger with a Bearer scheme. See backend/docs/auth.md.
builder.AddSimGuardSecurity();

var app = builder.Build();

if (app.Services.GetRequiredService<JwtSettings>().UsesEphemeralKey)
{
    app.Logger.LogWarning(
        "Jwt:Key is not set. Using a random signing key; tokens stop working when the API restarts.");
}

await IdentitySeeder.SeedAsync(app.Services);

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
.RequireAuthorization(AuthPolicies.CanRead);

var api = app.MapGroup("/api");

api.MapDepartmentEndpoints();
api.MapEmployeeEndpoints();
api.MapSimEndpoints();
api.MapAllocationEndpoints();
api.MapAuthEndpoints();
api.MapUserEndpoints();

app.Run();
