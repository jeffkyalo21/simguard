using System.Text.Json;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Models;

namespace SimGuard.Api.Common;

// Action names written to audit_logs. Documented in
// backend/docs/api-alerts-risk-audit.md.
public static class AuditActions
{
    public const string SimCreated = "sim.created";
    public const string SimUpdated = "sim.updated";
    public const string SimStatusChanged = "sim.status_changed";
    public const string SimTierChanged = "sim.tier_changed";
    public const string SimDeleted = "sim.deleted";
    public const string CarrierCreated = "carrier.created";
    public const string CarrierRenamed = "carrier.renamed";
    public const string CarrierDeleted = "carrier.deleted";
    public const string AllocationCreated = "allocation.created";
    public const string AllocationReturned = "allocation.returned";
    public const string DepartmentCreated = "department.created";
    public const string DepartmentUpdated = "department.updated";
    public const string DepartmentDeleted = "department.deleted";
    public const string EmployeeCreated = "employee.created";
    public const string EmployeeUpdated = "employee.updated";
    public const string EmployeeDeleted = "employee.deleted";
    public const string UserCreated = "user.created";
    public const string UserRoleChanged = "user.role_changed";
    public const string UserDisabled = "user.disabled";
    public const string UserEnabled = "user.enabled";
    public const string UserMfaReset = "user.mfa_reset";
    public const string UserPasswordReset = "user.password_reset";
    public const string UserUnlocked = "user.unlocked";
    public const string UserSignInEmailSent = "user.signin_email_sent";
    public const string AlertStatusChanged = "alert.status_changed";
    public const string ImportCommitted = "import.committed";
    public const string AccessRequestReviewed = "user.access_request_reviewed";
    public const string DemoSeeded = "dev.demo_seeded";
    public const string ScoringRunRequested = "scoring.run_requested";
    public const string ScoringRunFailed = "scoring.run_failed";
    public const string TierRecommendationRunStarted = "tier_recommendation.run_started";
    public const string TierRecommendationSettingsChanged = "tier_recommendation.settings_changed";
    public const string TierRecommendationDismissed = "tier_recommendation.dismissed";
    public const string TierRecommendationApplied = "tier_recommendation.applied";
}

// Audit categories. Admin (the SimGuard operator) reads only "system" rows.
public static class AuditCategories
{
    public const string System = "system";
    public const string Organisation = "organisation";

    // Same rule as the AddAuditCategory backfill: actions starting with
    // user, auth or role are system, everything else is organisation.
    // Scoring runs (issue #14) operate SimGuard and are system too.
    public static string For(string action) =>
        action.StartsWith("user", StringComparison.Ordinal)
        || action.StartsWith("auth", StringComparison.Ordinal)
        || action.StartsWith("role", StringComparison.Ordinal)
        || action.StartsWith("scoring.", StringComparison.Ordinal)
            ? System
            : Organisation;
}

// Writes audit_logs rows for the signed-in caller. Call it inside the same
// transaction as the change it records, before the commit, so the change
// and its audit row are saved together. Never pass passwords, hashes,
// tokens or MFA secrets as values.
public sealed class AuditWriter(SimGuardDbContext db, IHttpContextAccessor httpContextAccessor)
{
    private const int EmailMaxLength = 256;
    private const int RoleMaxLength = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Actor of rows written by background work (scheduled, import or
    // automatic runs): no user, ActorEmail "system".
    public const string SystemActor = "system";

    // Adds a row for background work: actor "system", category system.
    public AuditLog AddSystem(string action, int? affectedSimId, object? oldValues, object? newValues)
    {
        var row = new AuditLog
        {
            ActorUserId = null,
            ActorEmail = SystemActor,
            Action = action,
            Category = AuditCategories.System,
            ActorRole = null,
            AffectedSimId = affectedSimId,
            OldValues = ToJson(oldValues),
            NewValues = ToJson(newValues),
            Timestamp = Timestamps.UtcNow()
        };

        db.AuditLogs.Add(row);
        return row;
    }

    // Adds the row to the context without saving it.
    public AuditLog Add(string action, int? affectedSimId, object? oldValues, object? newValues)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var email = principal?.FindFirst(TokenService.EmailClaim)?.Value?.Trim();
        var role = principal?.FindFirst(TokenService.RoleClaim)?.Value?.Trim();

        var row = new AuditLog
        {
            ActorUserId = principal is null ? null : TokenService.GetUserId(principal),
            ActorEmail = string.IsNullOrEmpty(email)
                ? null
                : email.Length > EmailMaxLength ? email[..EmailMaxLength] : email,
            Action = action,
            Category = AuditCategories.For(action),
            ActorRole = string.IsNullOrEmpty(role)
                ? null
                : role.Length > RoleMaxLength ? role[..RoleMaxLength] : role,
            AffectedSimId = affectedSimId,
            OldValues = ToJson(oldValues),
            NewValues = ToJson(newValues),
            Timestamp = Timestamps.UtcNow()
        };

        db.AuditLogs.Add(row);
        return row;
    }

    // Adds the row and saves it.
    public async Task WriteAsync(
        string action,
        int? affectedSimId,
        object? oldValues,
        object? newValues,
        CancellationToken cancellationToken)
    {
        Add(action, affectedSimId, oldValues, newValues);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static JsonDocument? ToJson(object? value) =>
        value is null ? null : JsonSerializer.SerializeToDocument(value, value.GetType(), JsonOptions);
}
