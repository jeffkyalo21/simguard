using System.Linq.Expressions;
using System.Text.Json;
using SimGuard.Api.Common;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

// Shapes documented in backend/docs/api-alerts-risk-audit.md.

public record AlertStatusRequest(string? Status, string? Resolution, string? ResolutionNote);

public record AlertListItem(
    int Id,
    int SimId,
    string SimPhoneNumber,
    string Type,
    decimal RiskScore,
    string RiskLevel,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    JsonDocument Reasons,
    string? ModelVersion,
    string? EmployeeName,
    string? DepartmentName)
{
    // Employee and department come from the SIM's current allocation as
    // correlated subqueries, so a page is one SQL statement.
    public static readonly Expression<Func<Alert, AlertListItem>> Projection =
        a => new AlertListItem(
            a.Id,
            a.SimId,
            a.Sim.PhoneNumber,
            a.Type,
            a.RiskScore,
            a.RiskLevel,
            a.Status,
            a.CreatedAt,
            a.ResolvedAt,
            a.Reasons,
            a.ModelVersion,
            a.Sim.Allocations
                .Where(x => x.ReturnDate == null)
                .OrderByDescending(x => x.AssignmentDate)
                .Select(x => x.Employee.Name)
                .FirstOrDefault(),
            a.Sim.Allocations
                .Where(x => x.ReturnDate == null)
                .OrderByDescending(x => x.AssignmentDate)
                .Select(x => x.Employee.Department.Name)
                .FirstOrDefault());
}

public record UserReference(int Id, string Name, string? Email);

public record AlertSimSummary(
    int Id,
    string PhoneNumber,
    string Iccid,
    string Operator,
    string PlanType,
    string Status);

public record RecentAlertItem(
    int Id,
    string Type,
    decimal RiskScore,
    string RiskLevel,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt);

public record AlertDetailResponse(
    int Id,
    int SimId,
    string SimPhoneNumber,
    string Type,
    decimal RiskScore,
    string RiskLevel,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    JsonDocument Reasons,
    string? ModelVersion,
    string? Resolution,
    string? ResolutionNote,
    UserReference? ResolvedBy,
    AlertSimSummary Sim,
    CurrentAllocationResponse? CurrentAllocation,
    IReadOnlyList<RecentAlertItem> RecentAlerts);

public record RiskScoreItem(
    long Id,
    int SimId,
    string SimPhoneNumber,
    DateOnly Date,
    decimal Probability,
    string Bucket,
    string RiskLevel,
    JsonDocument Reasons,
    string? ModelVersion,
    DateTime CreatedAt)
{
    public static readonly Expression<Func<RiskScore, RiskScoreItem>> Projection =
        r => new RiskScoreItem(
            r.Id,
            r.SimId,
            r.Sim.PhoneNumber,
            r.Date,
            r.Probability,
            r.Bucket,
            r.Bucket == RiskBuckets.Flag
                ? RiskLevels.High
                : r.Bucket == RiskBuckets.Escalated ? RiskLevels.Medium : RiskLevels.Low,
            r.Reasons,
            r.ModelVersion,
            r.CreatedAt);
}

public record RiskLatest(
    DateOnly Date,
    decimal Probability,
    string Bucket,
    string RiskLevel,
    JsonDocument Reasons,
    string? ModelVersion,
    DateTime CreatedAt);

public record RiskHistoryPoint(DateOnly Date, decimal Probability, string Bucket);

public record SimRiskResponse(
    int SimId,
    int Days,
    RiskLatest? Latest,
    IReadOnlyList<RiskHistoryPoint> History);

public record ActivityItem(
    string Kind,
    DateTime OccurredAt,
    string Summary,
    string? ActorEmail,
    string? Action,
    string ReferenceType,
    int ReferenceId);

public record DashboardSummaryResponse(
    int Days,
    DateTime From,
    DateTime To,
    int ActiveSims,
    int Flagged,
    int ToReview,
    int Resolved,
    IReadOnlyDictionary<string, int> AlertsByType,
    IReadOnlyList<AlertListItem> NeedsReview,
    IReadOnlyList<object> TierChanges,
    object? DataWaste);

public record AuditLogItem(
    int Id,
    DateTime Timestamp,
    int? ActorUserId,
    string? ActorEmail,
    int? EmployeeId,
    string Action,
    int? AffectedSimId,
    string? AffectedSimPhoneNumber,
    JsonDocument? OldValues,
    JsonDocument? NewValues);

public record SeedDemoResponse(
    int SimsUsed,
    int AlertsCreated,
    int RiskScoresCreated,
    int AlertsRemoved,
    int RiskScoresRemoved);
