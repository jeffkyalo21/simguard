using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

// Used by POST and PUT. Status is deliberately absent: new SIMs start as
// "available" and status only changes through PATCH /sims/{id}/status or
// the allocation workflow.
public record SimRequest(
    string? PhoneNumber,
    string? Iccid,
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

public record SimResponse(
    int Id,
    string PhoneNumber,
    string Iccid,
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
            s.Operator,
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
