using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

// Used by POST and PUT. Status is deliberately absent: new SIMs start as
// "available". Status comes from the carrier data (imports and the carrier
// feed) or the allocation workflow; SimGuard has no endpoint to change it.
//
// The carrier is given by CarrierId (what the frontend SIM form sends) or,
// for older clients, by Operator, the carrier name (case-insensitive).
// CarrierId wins when both are set. Unknown carriers are a 400.
public record SimRequest(
    string? PhoneNumber,
    string? Iccid,
    int? CarrierId,
    string? Operator,
    string? PlanType,
    decimal? MonthlyCost);

public record SimTierRequest(string? Tier);

// PATCH /sims/{id}/tier. PreviousTier is null when the SIM had none.
public record SimTierResponse(int SimId, string Tier, string? PreviousTier);

public record CurrentAllocationResponse(
    int AllocationId,
    int EmployeeId,
    string EmployeeName,
    string DepartmentName,
    DateTime AssignedAt);

// One tier's bundle per 30-day billing cycle (GET /tiers and SIM responses).
public record TierAllowanceResponse(
    string Tier,
    int DataMb,
    int VoiceMinutes,
    int Sms,
    decimal MonthlyCost)
{
    public static readonly Expression<Func<TierAllowance, TierAllowanceResponse>> Projection =
        t => new TierAllowanceResponse(t.Tier, t.DataMb, t.VoiceMinutes, t.Sms, t.MonthlyCost);
}

// Operator is the carrier's name, kept so existing screens keep working.
// Tier is the ML plan tier (field, manager, professional, support) or null.
// MonthlyCost is the stored cost, or the tier's cost when the stored cost is
// 0 (imported SIMs carry no cost). Allowance is the tier's bundle, null
// while the SIM has no tier.
public record SimResponse(
    int Id,
    string PhoneNumber,
    string Iccid,
    int CarrierId,
    string Operator,
    string PlanType,
    decimal MonthlyCost,
    string Status,
    string? Tier,
    DateTime CreatedAt,
    CurrentAllocationResponse? CurrentAllocation,
    TierAllowanceResponse? Allowance)
{
    // The current allocation is a correlated subquery, so a page of SIMs
    // is still fetched with a single SQL statement.
    public static readonly Expression<Func<Sim, SimResponse>> Projection =
        s => new SimResponse(
            s.Id,
            s.PhoneNumber,
            s.Iccid,
            s.CarrierId,
            s.Carrier.Name,
            s.PlanType,
            s.MonthlyCost != 0 || s.TierAllowance == null ? s.MonthlyCost : s.TierAllowance.MonthlyCost,
            s.Status,
            s.Tier,
            s.CreatedAt,
            s.Allocations
                .Where(a => a.ReturnDate == null)
                .OrderByDescending(a => a.AssignmentDate)
                .Select(a => new CurrentAllocationResponse(
                    a.Id,
                    a.EmployeeId,
                    a.Employee.Name,
                    a.Employee.Department.Name,
                    a.AssignmentDate))
                .FirstOrDefault(),
            s.TierAllowance == null
                ? null
                : new TierAllowanceResponse(
                    s.TierAllowance.Tier,
                    s.TierAllowance.DataMb,
                    s.TierAllowance.VoiceMinutes,
                    s.TierAllowance.Sms,
                    s.TierAllowance.MonthlyCost));
}
