namespace SimGuard.Api.Common;

public static class Timestamps
{
    // Current UTC time truncated to microseconds, the precision of
    // PostgreSQL timestamptz, so the value returned on create matches
    // what later reads return.
    public static DateTime UtcNow()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }
}
