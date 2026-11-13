using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SimGuard.CarrierMock;
using SimGuard.CarrierMock.Release;

// A stand-in for real carrier usage feeds. Serves the demo data from files,
// one API key and one slice of SIMs per carrier, and releases one more day
// every night. Contract in docs/carrier-feed.md. Demo tool: not for production.

var builder = WebApplication.CreateBuilder(args);

builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var dataDir = config["CarrierMock:DataDir"]
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ml-service", "data", "import_samples"));
    var sessionsFile = config["CarrierMock:SessionsFile"]
        ?? Path.GetFullPath(Path.Combine(dataDir, "..", "synthetic_cdr_sessions.csv"));
    return new MockData(dataDir, sessionsFile);
});

builder.Services.AddSingleton(sp =>
    ReleaseSettings.FromConfiguration(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<MockData>().Dates));

builder.Services.AddSingleton(sp => new ReleaseState(
    sp.GetRequiredService<ReleaseSettings>(),
    sp.GetRequiredService<MockData>().Dates,
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger("SimGuard.CarrierMock.Release")));

builder.Services.AddSingleton<MockState>();
builder.Services.AddHostedService<ReleaseScheduler>();

var app = builder.Build();

// Resolve everything now so a bad setting or a missing data folder stops the start.
var data = app.Services.GetRequiredService<MockData>();
var settings = app.Services.GetRequiredService<ReleaseSettings>();
var release = app.Services.GetRequiredService<ReleaseState>();
var state = app.Services.GetRequiredService<MockState>();
var keys = CarrierKeys(app.Configuration, data);

app.MapGet("/", () => Results.Json(new
{
    name = "SimGuard carrier mock",
    sims = data.SimCount,
    carriers = data.CarrierNames,
    status = release.AtEnd ? ReleaseStatuses.NoMoreDays : ReleaseStatuses.Releasing
}));

var v1 = app.MapGroup("/v1/usage").AddEndpointFilter(async (context, next) =>
{
    var http = context.HttpContext;
    var carrier = Authenticate(http.Request.Headers["X-Api-Key"].ToString());

    if (carrier is null)
    {
        return Problem(StatusCodes.Status401Unauthorized, "invalid_api_key", "The API key is missing or wrong.");
    }

    http.Items["carrier"] = carrier;

    if (state.Trigger(Faults.Slow))
    {
        await Task.Delay(state.SlowDelayMs, http.RequestAborted);
    }

    if (state.Trigger(Faults.RateLimit))
    {
        http.Response.Headers.RetryAfter = "1";
        return Problem(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Retry after 1 second.");
    }

    if (state.Trigger(Faults.ServerError))
    {
        return Problem(StatusCodes.Status500InternalServerError, "server_error", "Simulated carrier outage.");
    }

    return await next(context);
});

v1.MapGet("/summary", (HttpContext http, string? date, int? page, int? pageSize) =>
{
    if (!TryDate(date, out var day, out var bad))
    {
        return bad!;
    }

    if (!release.IsAvailable(day))
    {
        return NotYet(day);
    }

    var carrier = (string)http.Items["carrier"]!;
    var rows = state.Trigger(Faults.EmptyDay) ? [] : data.Summary(carrier, release.ToDataDate(day));
    var (p, size) = Paging(page, pageSize);

    return Results.Json(new
    {
        date = day.ToString("yyyy-MM-dd"),
        page = p,
        pageSize = size,
        total = rows.Count,
        items = rows.Skip((p - 1) * size).Take(size).Select(r => new
        {
            iccid = r.Iccid,
            date = release.ToServedDate(r.Date).ToString("yyyy-MM-dd"),
            dataMb = r.TotalMb,
            voiceMinutes = r.VoiceMinutesUsed,
            sms = r.SmsCount,
            roamingMb = RoamingMb(r),
            billedMb = r.BilledMb,
            downloadMb = r.DownloadMb,
            uploadMb = r.UploadMb,
            dataSessionCount = r.DataSessionCount,
            dataDurationS = r.DataDurationS,
            hotspotSessions = r.HotspotSessions,
            publicApnSessions = r.PublicApnSessions,
            uniqueTowers = r.UniqueTowers,
            geoFlagCount = r.GeoFlagCount,
            peakMb = r.PeakMb,
            offpeakMb = r.OffpeakMb,
            roamingSessions = r.RoamingSessions,
            tier = r.Tier,
            voiceMinutesRemaining = r.VoiceMinutesRemaining,
            smsRemaining = r.SmsRemaining
        })
    });
});

v1.MapGet("/sessions", (HttpContext http, string? iccid, string? date, int? page, int? pageSize) =>
{
    if (string.IsNullOrWhiteSpace(iccid))
    {
        return Problem(StatusCodes.Status400BadRequest, "bad_request", "iccid is required.");
    }

    if (!TryDate(date, out var day, out var bad))
    {
        return bad!;
    }

    if (!release.IsAvailable(day))
    {
        return NotYet(day);
    }

    var carrier = (string)http.Items["carrier"]!;
    var owned = string.Equals(data.CarrierOf(iccid), carrier, StringComparison.OrdinalIgnoreCase);
    var sessions = owned ? data.Sessions(iccid, release.ToDataDate(day), release.DateShiftDays) : [];
    var (p, size) = Paging(page, pageSize);

    return Results.Json(new
    {
        iccid,
        date = day.ToString("yyyy-MM-dd"),
        page = p,
        pageSize = size,
        total = sessions.Count,
        items = sessions.Skip((p - 1) * size).Take(size)
    });
});

// Demo controls. Every route needs X-Admin-Key = MOCK_ADMIN_KEY; when that is
// not set, any carrier's key in X-Api-Key is accepted instead. Checked in
// middleware, before the request body is read.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/admin"), branch => branch.Use(async (context, next) =>
{
    if (AdminAllowed(context.Request))
    {
        await next(context);
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(new { code = "invalid_admin_key", message = "The admin key is missing or wrong." });
}));

var admin = app.MapGroup("/admin");

admin.MapGet("/status", () => Results.Json(release.Status()));

admin.MapPost("/release-next-day", () => Results.Json(Outcome(release.ReleaseNext("manual"))));

admin.MapPost("/reset-pointer", (PointerBody? body) =>
{
    if (body?.Day is < 0)
    {
        return Problem(StatusCodes.Status400BadRequest, "bad_request", "day must be 0 or more.");
    }

    return Results.Json(Outcome(release.Reset(body?.Day)));
});

admin.MapGet("/state", () => Results.Json(new { release = release.Status(), faults = state.Snapshot() }));

// Older names, kept for the smoke script: advance-day is release-next-day,
// set-date moves the pointer to the last data day on or before the date.
admin.MapPost("/advance-day", () => Results.Json(Outcome(release.ReleaseNext("manual"))));

admin.MapPost("/set-date", (DateBody body) =>
{
    if (!DateOnly.TryParseExact(body.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
    {
        return Problem(StatusCodes.Status400BadRequest, "bad_request", "date must be yyyy-MM-dd.");
    }

    return Results.Json(Outcome(release.SetThroughDate(day)));
});

admin.MapPost("/fault", (FaultBody body) =>
{
    if (!Faults.All.Contains(body.Fault))
    {
        return Problem(StatusCodes.Status400BadRequest, "bad_request",
            $"fault must be one of {string.Join(", ", Faults.All)}.");
    }

    state.SetFault(body.Fault, body.Enabled ?? true, body.Count, body.DelayMs);
    return Results.Json(state.Snapshot());
});

admin.MapPost("/reset", () =>
{
    state.ClearFaults();
    return Results.Json(state.Snapshot());
});

app.Run();

IResult NotYet(DateOnly day) => Problem(StatusCodes.Status404NotFound, "date_not_available",
    release.AtEnd && data.LastDate is not null
        ? $"No data for {day:yyyy-MM-dd}: no more days, the data ends at {release.ToServedDate(data.LastDate!.Value):yyyy-MM-dd}."
        : $"No data for {day:yyyy-MM-dd} yet.");

string? Authenticate(string supplied)
{
    if (string.IsNullOrEmpty(supplied))
    {
        return null;
    }

    foreach (var (carrier, key) in keys)
    {
        if (SameKey(supplied, key))
        {
            return carrier;
        }
    }

    return null;
}

bool AdminAllowed(HttpRequest request) =>
    settings.AdminKey is { } adminKey
        ? SameKey(request.Headers["X-Admin-Key"].ToString(), adminKey)
        : Authenticate(request.Headers["X-Api-Key"].ToString()) is not null;

static bool SameKey(string supplied, string expected) =>
    supplied.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));

// Each carrier has its own key: CarrierMock:Keys:<Carrier name>. A carrier
// without a configured key gets a random one, printed once at start (local runs only;
// the carrier server's mock.env sets every key).
static Dictionary<string, string> CarrierKeys(IConfiguration config, MockData data)
{
    var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var name in data.CarrierNames)
    {
        var key = config[$"CarrierMock:Keys:{name}"];

        if (string.IsNullOrWhiteSpace(key))
        {
            key = "mock-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            Console.WriteLine($"Generated API key for {name}: {key}");
        }

        keys[name] = key;
    }

    return keys;
}

static object Outcome(ReleaseOutcome o) => new
{
    status = o.Status,
    releasedThroughDay = o.ReleasedThroughDay,
    releasedThroughDate = o.ReleasedThroughDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    daysLeft = o.DaysLeft
};

static IResult Problem(int status, string code, string message) =>
    Results.Json(new { code, message }, statusCode: status);

static bool TryDate(string? value, out DateOnly day, out IResult? bad)
{
    bad = null;

    if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
    {
        return true;
    }

    bad = Problem(StatusCodes.Status400BadRequest, "bad_request", "date is required as yyyy-MM-dd.");
    return false;
}

static (int Page, int Size) Paging(int? page, int? pageSize) =>
    (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 500, 1, 1000));

// The data files carry roaming sessions, not roaming volume, so the
// roaming share of the day's volume is estimated from the session share.
static double RoamingMb(UsageRow r) =>
    r.RoamingSessions <= 0 || r.DataSessionCount <= 0
        ? 0
        : Math.Round(r.TotalMb * Math.Min(1.0, (double)r.RoamingSessions / r.DataSessionCount), 4);

record DateBody(string? Date);

record PointerBody(int? Day);

record FaultBody(string Fault, bool? Enabled, int? Count, int? DelayMs);

public partial class Program;
