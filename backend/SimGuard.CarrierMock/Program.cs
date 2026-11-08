using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SimGuard.CarrierMock;

// A stand-in for real carrier usage feeds. Serves the demo data from files,
// one API key and one slice of SIMs per carrier. Contract in
// docs/carrier-feed.md. Demo tool: not for production.

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var dataDir = config["CarrierMock:DataDir"]
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ml-service", "data", "import_samples"));
var sessionsFile = config["CarrierMock:SessionsFile"]
    ?? Path.GetFullPath(Path.Combine(dataDir, "..", "synthetic_cdr_sessions.csv"));

var data = new MockData(dataDir, sessionsFile);

// Each carrier has its own key: CarrierMock:Keys:<Carrier name>. A carrier
// without a configured key gets a random one, printed once at start.
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

var startDate = DateOnly.TryParseExact(config["CarrierMock:StartDate"], "yyyy-MM-dd",
    CultureInfo.InvariantCulture, DateTimeStyles.None, out var configured)
    ? configured
    : data.FirstDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

var state = new MockState(startDate);

var app = builder.Build();

app.MapGet("/", () => Results.Json(new { name = "SimGuard carrier mock", sims = data.SimCount, carriers = data.CarrierNames }));

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

    if (day > state.CurrentDate)
    {
        return Problem(StatusCodes.Status404NotFound, "date_not_available", $"No data for {day:yyyy-MM-dd} yet.");
    }

    var carrier = (string)http.Items["carrier"]!;
    var rows = state.Trigger(Faults.EmptyDay) ? [] : data.Summary(carrier, day);
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
            date = r.Date.ToString("yyyy-MM-dd"),
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

    if (day > state.CurrentDate)
    {
        return Problem(StatusCodes.Status404NotFound, "date_not_available", $"No data for {day:yyyy-MM-dd} yet.");
    }

    var carrier = (string)http.Items["carrier"]!;
    var owned = string.Equals(data.CarrierOf(iccid), carrier, StringComparison.OrdinalIgnoreCase);
    var sessions = owned ? data.Sessions(iccid, day) : [];
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

// Demo controls. They have no key: run the mock on localhost only.
var admin = app.MapGroup("/admin");

admin.MapGet("/state", () => Results.Json(state.Snapshot()));

admin.MapPost("/advance-day", () =>
{
    state.CurrentDate = state.CurrentDate.AddDays(1);
    return Results.Json(state.Snapshot());
});

admin.MapPost("/set-date", (DateBody body) =>
{
    if (!DateOnly.TryParseExact(body.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
    {
        return Problem(StatusCodes.Status400BadRequest, "bad_request", "date must be yyyy-MM-dd.");
    }

    state.CurrentDate = day;
    return Results.Json(state.Snapshot());
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

string? Authenticate(string supplied)
{
    if (string.IsNullOrEmpty(supplied))
    {
        return null;
    }

    var bytes = Encoding.UTF8.GetBytes(supplied);

    foreach (var (carrier, key) in keys)
    {
        if (CryptographicOperations.FixedTimeEquals(bytes, Encoding.UTF8.GetBytes(key)))
        {
            return carrier;
        }
    }

    return null;
}

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

record FaultBody(string Fault, bool? Enabled, int? Count, int? DelayMs);
