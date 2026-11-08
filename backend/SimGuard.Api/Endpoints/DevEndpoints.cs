using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

// Development helpers. Program.cs maps these only when the environment is
// Development, so they do not exist in any other environment.
public static class DevEndpoints
{
    // Marks every row this seeder writes. Reseeding deletes only these rows.
    public const string DemoModelVersion = "demo";

    private const int DemoAlertCount = 60;
    private const int DemoScoredSimCount = 30;
    private const int DemoHistoryDays = 90;
    private const int Seed = 1313;

    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/dev/seed-demo", SeedDemo)
            .WithTags("Development")
            .WithName("SeedDemo")
            .RequireAuthorization(AuthPolicies.DevTools)
            .Produces<SeedDemoResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> SeedDemo(
        SimGuardDbContext db,
        AuditWriter audit,
        HttpContext httpContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var simIds = await db.Sims
            .OrderBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (simIds.Count == 0)
        {
            return ApiProblems.Conflict("There are no SIMs yet. Create some SIMs before seeding demo data.");
        }

        var callerId = TokenService.GetUserId(httpContext.User);
        var now = Timestamps.UtcNow();
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var random = new Random(Seed);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var alertsRemoved = await db.Alerts
            .Where(a => a.ModelVersion == DemoModelVersion)
            .ExecuteDeleteAsync(cancellationToken);

        var scoresRemoved = await db.RiskScores
            .Where(r => r.ModelVersion == DemoModelVersion)
            .ExecuteDeleteAsync(cancellationToken);

        var alerts = new List<Alert>(DemoAlertCount);

        for (var i = 0; i < DemoAlertCount; i++)
        {
            var type = AlertTypes.All[i % 4];
            var simId = simIds[random.Next(simIds.Count)];
            var createdAt = now
                .AddDays(-random.Next(0, DemoHistoryDays))
                .AddMinutes(-random.Next(0, 24 * 60));

            // Roughly 40% flagged, 25% escalated, 35% cleared.
            var roll = random.Next(100);
            var status = roll < 40
                ? AlertStatuses.Flagged
                : roll < 65 ? AlertStatuses.Escalated : AlertStatuses.Cleared;

            // A cleared alert was opened as flagged or escalated.
            var openedAs = status == AlertStatuses.Cleared
                ? (random.Next(2) == 0 ? AlertStatuses.Flagged : AlertStatuses.Escalated)
                : status;

            var score = openedAs == AlertStatuses.Flagged
                ? 0.80m + random.Next(0, 20) / 100m
                : 0.55m + random.Next(0, 25) / 100m;

            var alert = new Alert
            {
                SimId = simId,
                Type = type,
                RiskScore = score,
                RiskLevel = RiskLevels.ForNewAlert(openedAs),
                Status = status,
                CreatedAt = createdAt,
                Reasons = Reasons(type, random),
                ModelVersion = DemoModelVersion
            };

            if (status == AlertStatuses.Cleared)
            {
                var resolvedAt = createdAt.AddHours(random.Next(1, 72));
                alert.ResolvedAt = resolvedAt > now ? now : resolvedAt;
                alert.ResolvedByUserId = callerId;

                if (random.Next(3) == 0)
                {
                    alert.Resolution = AlertResolutions.ConfirmedMisuse;
                    alert.ResolutionNote = "Demo: misuse confirmed with the line manager.";
                }
                else
                {
                    alert.Resolution = AlertResolutions.FalsePositive;
                    alert.ResolutionNote = "Demo: activity explained by the employee.";
                }
            }

            alerts.Add(alert);
        }

        db.Alerts.AddRange(alerts);

        // Daily scores for the first SIMs. The demo picks a bucket for each
        // day from the SIM's profile, then a probability that fits it; the
        // real values come from the ML service (issue #14).
        var scoredSims = simIds.Take(DemoScoredSimCount).ToList();
        var scores = new List<RiskScore>(scoredSims.Count * DemoHistoryDays);

        foreach (var simId in scoredSims)
        {
            var profile = random.Next(10); // 0..5 quiet, 6..8 watch, 9 risky
            var type = AlertTypes.All[random.Next(4)];

            for (var d = DemoHistoryDays - 1; d >= 0; d--)
            {
                var roll = random.Next(100);
                var bucket = profile switch
                {
                    <= 5 => roll < 95 ? RiskBuckets.Clear : RiskBuckets.Escalated,
                    <= 8 => roll < 70 ? RiskBuckets.Clear : roll < 95 ? RiskBuckets.Escalated : RiskBuckets.Flag,
                    _ => roll < 40 ? RiskBuckets.Clear : roll < 70 ? RiskBuckets.Escalated : RiskBuckets.Flag
                };

                var probability = bucket switch
                {
                    RiskBuckets.Flag => 0.8000m + random.Next(0, 2000) / 10000m,
                    RiskBuckets.Escalated => 0.5000m + random.Next(0, 3000) / 10000m,
                    _ => 0.0100m + random.Next(0, 4000) / 10000m
                };

                scores.Add(new RiskScore
                {
                    SimId = simId,
                    Date = today.AddDays(-d),
                    Probability = probability,
                    Bucket = bucket,
                    Reasons = Reasons(type, random),
                    ModelVersion = DemoModelVersion,
                    CreatedAt = now
                });
            }
        }

        db.RiskScores.AddRange(scores);

        var response = new SeedDemoResponse(
            scoredSims.Count,
            alerts.Count,
            scores.Count,
            alertsRemoved,
            scoresRemoved);

        audit.Add(AuditActions.DemoSeeded, null, null, response);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(response);
    }

    // Top reasons that fit each alert type, with descending contributions.
    private static JsonDocument Reasons(string type, Random random)
    {
        (string Feature, double Min, double Max, string Direction)[] features = type switch
        {
            AlertTypes.InactiveBilling =>
            [
                ("daysSinceLastActivity", 30, 120, "up"),
                ("monthlyCost", 1000, 5000, "up"),
                ("dataUsageMb30d", 0, 5, "down")
            ],
            AlertTypes.Sharing =>
            [
                ("distinctDevices7d", 2, 6, "up"),
                ("distinctCellsPerDay", 10, 40, "up"),
                ("concurrentSessions", 2, 5, "up")
            ],
            AlertTypes.Reselling =>
            [
                ("distinctCalledNumbers7d", 150, 600, "up"),
                ("outgoingCallsPerDay", 80, 300, "up"),
                ("smsVolume7d", 500, 3000, "up")
            ],
            _ =>
            [
                ("activityWhileUnassignedHours", 5, 100, "up"),
                ("nightUsageShare", 0.4, 0.9, "up"),
                ("dataUsageMb30d", 2000, 20000, "up")
            ]
        };

        var contribution = 0.25 + random.NextDouble() * 0.15;
        var reasons = new List<object>(features.Length);

        foreach (var (feature, min, max, direction) in features)
        {
            var value = min + random.NextDouble() * (max - min);

            reasons.Add(new
            {
                feature,
                value = max <= 1 ? Math.Round(value, 2) : Math.Round(value),
                contribution = Math.Round(contribution, 2),
                direction
            });

            contribution *= 0.6;
        }

        return JsonSerializer.SerializeToDocument(reasons);
    }
}
