using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

public static class DepartmentEndpoints
{
    private const int NameMaxLength = 100;

    public static IEndpointRouteBuilder MapDepartmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/departments")
            .WithTags("Departments");

        group.MapGet("/", GetDepartments)
            .WithName("GetDepartments")
            .Produces<PagedResult<DepartmentResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:int}", GetDepartment)
            .WithName("GetDepartment")
            .Produces<DepartmentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateDepartment)
            .WithName("CreateDepartment")
            .Produces<DepartmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:int}", UpdateDepartment)
            .WithName("UpdateDepartment")
            .Produces<DepartmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:int}", DeleteDepartment)
            .WithName("DeleteDepartment")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:int}/employees", GetDepartmentEmployees)
            .WithName("GetDepartmentEmployees")
            .Produces<PagedResult<EmployeeResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GetDepartments(
        SimGuardDbContext db,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var result = await db.Departments
            .AsNoTracking()
            .OrderBy(d => d.Id)
            .Select(DepartmentResponse.Projection)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetDepartment(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var department = await db.Departments
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(DepartmentResponse.Projection)
            .FirstOrDefaultAsync(cancellationToken);

        return department is null
            ? ApiProblems.NotFound($"Department {id} was not found.")
            : TypedResults.Ok(department);
    }

    private static async Task<IResult> CreateDepartment(
        DepartmentRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = errors.RequiredString("name", request.Name, NameMaxLength);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        if (await NameExistsAsync(db, name, excludeId: null, cancellationToken))
        {
            return ApiProblems.Conflict($"A department named '{name}' already exists.");
        }

        var department = new Department { Name = name };

        db.Departments.Add(department);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/departments/{department.Id}",
            new DepartmentResponse(department.Id, department.Name, 0));
    }

    private static async Task<IResult> UpdateDepartment(
        int id,
        DepartmentRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = errors.RequiredString("name", request.Name, NameMaxLength);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var department = await db.Departments
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (department is null)
        {
            return ApiProblems.NotFound($"Department {id} was not found.");
        }

        if (await NameExistsAsync(db, name, excludeId: id, cancellationToken))
        {
            return ApiProblems.Conflict($"A department named '{name}' already exists.");
        }

        department.Name = name;
        await db.SaveChangesAsync(cancellationToken);

        var employeeCount = await db.Employees
            .CountAsync(e => e.DepartmentId == id, cancellationToken);

        return TypedResults.Ok(new DepartmentResponse(department.Id, department.Name, employeeCount));
    }

    private static async Task<IResult> DeleteDepartment(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var department = await db.Departments
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (department is null)
        {
            return ApiProblems.NotFound($"Department {id} was not found.");
        }

        var employeeCount = await db.Employees
            .CountAsync(e => e.DepartmentId == id, cancellationToken);

        if (employeeCount > 0)
        {
            return ApiProblems.Conflict(
                $"Department {id} still has {employeeCount} employee(s). Move or delete them first.");
        }

        db.Departments.Remove(department);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetDepartmentEmployees(
        int id,
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

        if (!await db.Departments.AnyAsync(d => d.Id == id, cancellationToken))
        {
            return ApiProblems.NotFound($"Department {id} was not found.");
        }

        var result = await EmployeeEndpoints
            .QueryEmployees(db, departmentId: id, search)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static Task<bool> NameExistsAsync(
        SimGuardDbContext db,
        string name,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        return db.Departments.AnyAsync(
            d => d.Name.ToLower() == lowered && (excludeId == null || d.Id != excludeId),
            cancellationToken);
    }
}
