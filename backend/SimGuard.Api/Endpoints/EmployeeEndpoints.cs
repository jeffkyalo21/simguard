using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

public static class EmployeeEndpoints
{
    private const int NameMaxLength = 150;
    private const int EmailMaxLength = 150;
    private const int JobRoleMaxLength = 100;

    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/employees")
            .WithTags("Employees");

        group.MapGet("/", GetEmployees)
            .WithName("GetEmployees")
            .Produces<PagedResult<EmployeeResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:int}", GetEmployee)
            .WithName("GetEmployee")
            .Produces<EmployeeResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateEmployee)
            .WithName("CreateEmployee")
            .Produces<EmployeeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:int}", UpdateEmployee)
            .WithName("UpdateEmployee")
            .Produces<EmployeeResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:int}", DeleteEmployee)
            .WithName("DeleteEmployee")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:int}/allocations", GetEmployeeAllocations)
            .WithName("GetEmployeeAllocations")
            .Produces<PagedResult<AllocationResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    // Shared with GET /departments/{id}/employees so both lists behave the same.
    internal static IQueryable<EmployeeResponse> QueryEmployees(
        SimGuardDbContext db,
        int? departmentId,
        string? search)
    {
        var query = db.Employees.AsNoTracking();

        if (departmentId is not null)
        {
            query = query.Where(e => e.DepartmentId == departmentId);
        }

        var pattern = SearchPattern.Contains(search);

        if (pattern is not null)
        {
            query = query.Where(e =>
                EF.Functions.ILike(e.Name, pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(e.Email, pattern, SearchPattern.EscapeCharacter));
        }

        return query
            .OrderBy(e => e.Id)
            .Select(EmployeeResponse.Projection);
    }

    private static async Task<IResult> GetEmployees(
        int? departmentId,
        string? search,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var result = await QueryEmployees(db, departmentId, search)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetEmployee(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(EmployeeResponse.Projection)
            .FirstOrDefaultAsync(cancellationToken);

        return employee is null
            ? ApiProblems.NotFound($"Employee {id} was not found.")
            : TypedResults.Ok(employee);
    }

    private static async Task<IResult> CreateEmployee(
        EmployeeRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var (values, validationError) = await ValidateAsync(request, db, cancellationToken);

        if (validationError is not null)
        {
            return validationError;
        }

        if (await EmailExistsAsync(db, values.Email, excludeId: null, cancellationToken))
        {
            return ApiProblems.Conflict($"An employee with email '{values.Email}' already exists.");
        }

        var employee = new Employee
        {
            Name = values.Name,
            Email = values.Email,
            DepartmentId = values.DepartmentId,
            JobRole = values.JobRole,
            CreatedAt = Timestamps.UtcNow()
        };

        db.Employees.Add(employee);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/employees/{employee.Id}",
            ToResponse(employee, values.DepartmentName));
    }

    private static async Task<IResult> UpdateEmployee(
        int id,
        EmployeeRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var (values, validationError) = await ValidateAsync(request, db, cancellationToken);

        if (validationError is not null)
        {
            return validationError;
        }

        var employee = await db.Employees
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (employee is null)
        {
            return ApiProblems.NotFound($"Employee {id} was not found.");
        }

        if (await EmailExistsAsync(db, values.Email, excludeId: id, cancellationToken))
        {
            return ApiProblems.Conflict($"An employee with email '{values.Email}' already exists.");
        }

        employee.Name = values.Name;
        employee.Email = values.Email;
        employee.DepartmentId = values.DepartmentId;
        employee.JobRole = values.JobRole;

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(employee, values.DepartmentName));
    }

    private static async Task<IResult> DeleteEmployee(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (employee is null)
        {
            return ApiProblems.NotFound($"Employee {id} was not found.");
        }

        if (await db.Allocations.AnyAsync(
                a => a.EmployeeId == id || a.AssignedBy == id,
                cancellationToken))
        {
            return ApiProblems.Conflict(
                $"Employee {id} appears in allocation history (as holder or assigner) and cannot be deleted.");
        }

        if (await db.AuditLogs.AnyAsync(l => l.UserId == id, cancellationToken))
        {
            return ApiProblems.Conflict(
                $"Employee {id} has audit log entries and cannot be deleted.");
        }

        db.Employees.Remove(employee);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetEmployeeAllocations(
        int id,
        bool? active,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        if (!await db.Employees.AnyAsync(e => e.Id == id, cancellationToken))
        {
            return ApiProblems.NotFound($"Employee {id} was not found.");
        }

        var result = await AllocationEndpoints
            .QueryAllocations(db, simId: null, employeeId: id, active)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<(ValidatedEmployee Values, IResult? Error)> ValidateAsync(
        EmployeeRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();

        var name = errors.RequiredString("name", request.Name, NameMaxLength);
        var email = errors.RequiredEmail("email", request.Email, EmailMaxLength);
        var departmentId = errors.RequiredId("departmentId", request.DepartmentId);
        var jobRole = errors.RequiredString("jobRole", request.JobRole, JobRoleMaxLength);

        string? departmentName = null;

        if (departmentId > 0)
        {
            departmentName = await db.Departments
                .Where(d => d.Id == departmentId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (departmentName is null)
            {
                errors.Add("departmentId", $"Department {departmentId} does not exist.");
            }
        }

        var values = new ValidatedEmployee(name, email, departmentId, departmentName ?? string.Empty, jobRole);

        return (values, errors.HasErrors ? errors.ToProblem() : null);
    }

    private static Task<bool> EmailExistsAsync(
        SimGuardDbContext db,
        string email,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        var lowered = email.ToLower();

        return db.Employees.AnyAsync(
            e => e.Email.ToLower() == lowered && (excludeId == null || e.Id != excludeId),
            cancellationToken);
    }

    private static EmployeeResponse ToResponse(Employee employee, string departmentName) =>
        new(
            employee.Id,
            employee.Name,
            employee.Email,
            employee.DepartmentId,
            departmentName,
            employee.JobRole,
            employee.CreatedAt);

    private sealed record ValidatedEmployee(
        string Name,
        string Email,
        int DepartmentId,
        string DepartmentName,
        string JobRole);
}
