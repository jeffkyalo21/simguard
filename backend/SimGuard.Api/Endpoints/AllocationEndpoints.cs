using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

// Allocations are history: there is no PUT or DELETE. They are created by
// POST /allocations and closed by POST /allocations/{id}/return.
public static class AllocationEndpoints
{
    public static IEndpointRouteBuilder MapAllocationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/allocations")
            .WithTags("Allocations");

        group.MapGet("/", GetAllocations)
            .WithName("GetAllocations")
            .Produces<PagedResult<AllocationResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:int}", GetAllocation)
            .WithName("GetAllocation")
            .Produces<AllocationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAllocation)
            .WithName("CreateAllocation")
            .Produces<AllocationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:int}/return", ReturnAllocation)
            .WithName("ReturnAllocation")
            .Produces<AllocationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    // Shared with GET /sims/{id}/allocations and GET /employees/{id}/allocations.
    internal static IQueryable<AllocationResponse> QueryAllocations(
        SimGuardDbContext db,
        int? simId,
        int? employeeId,
        bool? active)
    {
        var query = db.Allocations.AsNoTracking();

        if (simId is not null)
        {
            query = query.Where(a => a.SimId == simId);
        }

        if (employeeId is not null)
        {
            query = query.Where(a => a.EmployeeId == employeeId);
        }

        if (active is not null)
        {
            query = active.Value
                ? query.Where(a => a.ReturnDate == null)
                : query.Where(a => a.ReturnDate != null);
        }

        return query
            .OrderBy(a => a.Id)
            .Select(AllocationResponse.Projection);
    }

    private static async Task<IResult> GetAllocations(
        int? simId,
        int? employeeId,
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

        var result = await QueryAllocations(db, simId, employeeId, active)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetAllocation(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var allocation = await FindAllocationResponseAsync(db, id, cancellationToken);

        return allocation is null
            ? ApiProblems.NotFound($"Allocation {id} was not found.")
            : TypedResults.Ok(allocation);
    }

    private static async Task<IResult> CreateAllocation(
        CreateAllocationRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();

        var simId = errors.RequiredId("simId", request.SimId);
        var employeeId = errors.RequiredId("employeeId", request.EmployeeId);
        var assignedBy = errors.RequiredId("assignedBy", request.AssignedBy);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        int allocationId;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // The row lock serialises concurrent requests for the same SIM:
            // the second request waits here, then sees the SIM as assigned.
            var sim = await SimEndpoints.LockSimAsync(db, simId, cancellationToken);

            if (sim is null)
            {
                errors.Add("simId", $"SIM {simId} does not exist.");
            }

            var employeeIds = new[] { employeeId, assignedBy };
            var existingEmployeeIds = await db.Employees
                .Where(e => employeeIds.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            if (!existingEmployeeIds.Contains(employeeId))
            {
                errors.Add("employeeId", $"Employee {employeeId} does not exist.");
            }

            if (!existingEmployeeIds.Contains(assignedBy))
            {
                errors.Add("assignedBy", $"Employee {assignedBy} does not exist.");
            }

            if (errors.HasErrors || sim is null)
            {
                return errors.ToProblem();
            }

            if (sim.Status != SimStatuses.Available)
            {
                return ApiProblems.Conflict(
                    $"SIM {simId} is '{sim.Status}' and cannot be assigned. Only available SIMs can be assigned.");
            }

            if (await db.Allocations.AnyAsync(
                    a => a.SimId == simId && a.ReturnDate == null,
                    cancellationToken))
            {
                return ApiProblems.Conflict($"SIM {simId} already has an active allocation.");
            }

            var allocation = new Allocation
            {
                SimId = simId,
                EmployeeId = employeeId,
                AssignedBy = assignedBy,
                AssignmentDate = Timestamps.UtcNow()
            };

            db.Allocations.Add(allocation);
            sim.Status = SimStatuses.Assigned;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            allocationId = allocation.Id;
        }

        return TypedResults.Created(
            $"/api/allocations/{allocationId}",
            await FindAllocationResponseAsync(db, allocationId, cancellationToken));
    }

    private static async Task<IResult> ReturnAllocation(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Lock the allocation so two concurrent returns cannot both succeed.
            var allocations = await db.Allocations
                .FromSql($"SELECT * FROM allocations WHERE id = {id} FOR UPDATE")
                .ToListAsync(cancellationToken);

            var allocation = allocations.SingleOrDefault();

            if (allocation is null)
            {
                return ApiProblems.NotFound($"Allocation {id} was not found.");
            }

            if (allocation.ReturnDate is not null)
            {
                return ApiProblems.Conflict(
                    $"Allocation {id} was already returned on {allocation.ReturnDate:O}.");
            }

            var sim = await SimEndpoints.LockSimAsync(db, allocation.SimId, cancellationToken);

            allocation.ReturnDate = Timestamps.UtcNow();

            // An inactive SIM stays inactive after its allocation is returned.
            if (sim is not null && sim.Status != SimStatuses.Inactive)
            {
                sim.Status = SimStatuses.Available;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return TypedResults.Ok(await FindAllocationResponseAsync(db, id, cancellationToken));
    }

    private static Task<AllocationResponse?> FindAllocationResponseAsync(
        SimGuardDbContext db,
        int id,
        CancellationToken cancellationToken) =>
        db.Allocations
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(AllocationResponse.Projection)
            .FirstOrDefaultAsync(cancellationToken);
}
