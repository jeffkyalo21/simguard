using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

// Used by POST and PUT. Status is deliberately absent: new SIMs start as
// "available" and status only changes through PATCH /sims/{id}/status or
// the allocation workflow.
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

public record SimStatusRequest(string? Status);

public record CurrentAllocationResponse(
    int AllocationId,
    int EmployeeId,
    string EmployeeName,
    string DepartmentName,
    DateTime AssignedAt);

// Operator is the carrier's name, kept so existing screens keep working.
public record SimResponse(
    int Id,
    string PhoneNumber,
    string Iccid,
    int CarrierId,
    string Operator,
    string PlanType,
    decimal MonthlyCost,
    string Status,
    DateTime CreatedAt,
    CurrentAllocationResponse? CurrentAllocation)
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
            s.MonthlyCost,
            s.Status,
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
                .FirstOrDefault());
}
