using SimGuard.Api.Endpoints;
using SimGuard.Api.Models;

namespace SimGuard.Api.Tests;

// The dashboard counts an alert in a 7, 30 or 90 day window when its first
// or its last detected day is inside the window. The as-of date is
// 2025-03-31, like the demo data.
public sealed class DashboardWindowTests
{
    private static readonly DateOnly AsOf = new(2025, 3, 31);

    private static bool In(Alert alert, int days)
    {
        var from = AsOf.AddDays(1 - days).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = AsOf.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
        return DashboardEndpoints.InWindow(from, to).Compile()(alert);
    }

    [Fact]
    public void Alert_first_detected_long_ago_but_still_active_shows_in_every_window()
    {
        var alert = new Alert { DetectedOn = AsOf.AddDays(-40), LastDetectedOn = AsOf.AddDays(-2) };

        Assert.True(In(alert, 7));
        Assert.True(In(alert, 30));
        Assert.True(In(alert, 90));
    }

    [Fact]
    public void Alert_with_both_dates_forty_days_back_shows_in_ninety_only()
    {
        var alert = new Alert { DetectedOn = AsOf.AddDays(-40), LastDetectedOn = AsOf.AddDays(-40) };

        Assert.False(In(alert, 7));
        Assert.False(In(alert, 30));
        Assert.True(In(alert, 90));
    }

    [Fact]
    public void Alert_without_dates_falls_back_to_the_stored_time()
    {
        var recent = new Alert { CreatedAt = AsOf.AddDays(-3).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc) };
        var old = new Alert { CreatedAt = AsOf.AddDays(-40).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc) };

        Assert.True(In(recent, 7));
        Assert.False(In(old, 7));
        Assert.False(In(old, 30));
        Assert.True(In(old, 90));
    }

    [Fact]
    public void Window_starts_on_its_first_day_and_ends_on_the_as_of_date()
    {
        Assert.True(In(new Alert { DetectedOn = AsOf.AddDays(-6), LastDetectedOn = AsOf.AddDays(-6) }, 7));
        Assert.False(In(new Alert { DetectedOn = AsOf.AddDays(-7), LastDetectedOn = AsOf.AddDays(-7) }, 7));
        Assert.False(In(new Alert { DetectedOn = AsOf.AddDays(1), LastDetectedOn = AsOf.AddDays(1) }, 7));
    }
}
