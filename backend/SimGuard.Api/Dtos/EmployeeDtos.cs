using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

public record EmployeeRequest(
    string? Name,
    string? Email,
    int? DepartmentId,
    string? JobRole);

public record EmployeeResponse(
    int Id,
    string Name,
    string Email,
    int DepartmentId,
    string DepartmentName,
    string JobRole,
    DateTime CreatedAt)
{
    public static readonly Expression<Func<Employee, EmployeeResponse>> Projection =
        e => new EmployeeResponse(
            e.Id,
            e.Name,
            e.Email,
            e.DepartmentId,
            e.Department.Name,
            e.JobRole,
            e.CreatedAt);
}
