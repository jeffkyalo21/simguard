namespace SimGuard.CarrierMock.Release;

// The release time as a wall clock time in the configured zone, never the
// server clock. A "slot" is the local date of one nightly release.
public sealed class ReleaseClock(TimeOnly releaseTime, TimeZoneInfo zone)
{
    public TimeZoneInfo Zone => zone;

    // The local date of the newest slot that has already started at nowUtc.
    public DateOnly LatestSlot(DateTimeOffset nowUtc)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        return TimeOnly.FromDateTime(local.DateTime) >= releaseTime ? today : today.AddDays(-1);
    }

    // The first release strictly after nowUtc.
    public DateTimeOffset NextRelease(DateTimeOffset nowUtc) => SlotStart(LatestSlot(nowUtc).AddDays(1));

    public DateTimeOffset SlotStart(DateOnly slot)
    {
        var local = slot.ToDateTime(releaseTime, DateTimeKind.Unspecified);

        // A release time inside a daylight saving gap does not exist that
        // night; the release happens at the end of the gap instead.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        var offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
