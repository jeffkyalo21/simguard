
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SimGuardDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new
{
    name = "SimGuard API",
    status = "Running"
}))
.WithName("GetApiStatus");

app.MapGet("/health/database", async (
    SimGuardDbContext db,
    CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);

    return canConnect
        ? Results.Ok(new
        {
            status = "connected",
            database = "PostgreSQL"
        })
        : Results.Problem("Database connection failed.");
})
.WithName("CheckDatabaseHealth");

app.Run();
