using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

public record DepartmentRequest(string? Name);

public record DepartmentResponse(
    int Id,
    string Name,
    int EmployeeCount)
{
    public static readonly Expression<Func<Department, DepartmentResponse>> Projection =
        d => new DepartmentResponse(d.Id, d.Name, d.Employees.Count());
}
