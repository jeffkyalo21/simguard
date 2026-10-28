using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

public record CreateAllocationRequest(
    int? SimId,
    int? EmployeeId,
    int? AssignedBy);

public record AllocationResponse(
    int Id,
    int SimId,
    string SimPhoneNumber,
    int EmployeeId,
    string EmployeeName,
    int AssignedBy,
    string AssignedByName,
    DateTime AssignmentDate,
    DateTime? ReturnDate,
    bool IsActive)
{
    public static readonly Expression<Func<Allocation, AllocationResponse>> Projection =
        a => new AllocationResponse(
            a.Id,
            a.SimId,
            a.Sim.PhoneNumber,
            a.EmployeeId,
            a.Employee.Name,
            a.AssignedBy,
            a.AssignedByEmployee.Name,
            a.AssignmentDate,
            a.ReturnDate,
            a.ReturnDate == null);
}
