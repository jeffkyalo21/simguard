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

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild",
    "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();

    return forecast;
})
.WithName("GetWeatherForecast");

app.MapGet("/", () => Results.Ok(new
{
    name = "SimGuard API",
    status = "Running"
}));

app.MapGet("/health/database", async (
    SimGuardDbContext db,
    CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);

    return canConnect
        ? Results.Ok(new { status = "connected", database = "PostgreSQL" })
        : Results.Problem("Database connection failed.");
});

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF =>
        32 + (int)(TemperatureC / 0.5556);
}
