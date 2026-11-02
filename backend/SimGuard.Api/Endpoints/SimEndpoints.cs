using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

public static class SimEndpoints
{
    private const int IccidMaxLength = 30;
    private const int OperatorMaxLength = 50;
    private const int PlanTypeMaxLength = 50;

    // numeric(10, 2)
    private const decimal MonthlyCostMax = 99_999_999.99m;

    public static IEndpointRouteBuilder MapSimEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/sims")
            .WithTags("SIMs");

        group.MapGet("/", GetSims)
            .WithName("GetSims")
            .Produces<PagedResult<SimResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:int}", GetSim)
            .WithName("GetSim")
            .Produces<SimResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateSim)
            .WithName("CreateSim")
            .Produces<SimResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:int}", UpdateSim)
            .WithName("UpdateSim")
            .Produces<SimResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:int}/status", UpdateSimStatus)
            .WithName("UpdateSimStatus")
            .Produces<SimResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:int}", DeleteSim)
            .WithName("DeleteSim")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:int}/allocations", GetSimAllocations)
            .WithName("GetSimAllocations")
            .Produces<PagedResult<AllocationResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    // Loads the SIM with a row lock (SELECT ... FOR UPDATE). Must be called
    // inside a transaction; the lock is held until it commits or rolls back.
    internal static async Task<Sim?> LockSimAsync(
        SimGuardDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        var sims = await db.Sims
            .FromSql($"SELECT * FROM sims WHERE id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return sims.SingleOrDefault();
    }

    private static async Task<IResult> GetSims(
        string? status,
        string? @operator,
        string? planType,
        string? search,
        int? page,
        int? pageSize,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var paging = PageRequest.FromQuery(page, pageSize, errors);
        var normalizedStatus = status?.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(normalizedStatus) && !SimStatuses.All.Contains(normalizedStatus))
        {
            errors.Add("status", $"status must be one of: {string.Join(", ", SimStatuses.All)}.");
        }

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var query = db.Sims.AsNoTracking();

        if (!string.IsNullOrEmpty(normalizedStatus))
        {
            query = query.Where(s => s.Status == normalizedStatus);
        }

        var operatorPattern = SearchPattern.Exact(@operator);

        if (operatorPattern is not null)
        {
            query = query.Where(s =>
                EF.Functions.ILike(s.Operator, operatorPattern, SearchPattern.EscapeCharacter));
        }

        var planTypePattern = SearchPattern.Exact(planType);

        if (planTypePattern is not null)
        {
            query = query.Where(s =>
                EF.Functions.ILike(s.PlanType, planTypePattern, SearchPattern.EscapeCharacter));
        }

        var searchPattern = SearchPattern.Contains(search);

        if (searchPattern is not null)
        {
            // Phone numbers are stored as +254..., so a search typed as 0712... is converted first.
            var phonePattern = SearchPattern.Contains(PhoneNumbers.ToSearchTerm(search)) ?? searchPattern;

            query = query.Where(s =>
                EF.Functions.ILike(s.PhoneNumber, phonePattern, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(s.Iccid, searchPattern, SearchPattern.EscapeCharacter));
        }

        var result = await query
            .OrderBy(s => s.Id)
            .Select(SimResponse.Projection)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetSim(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var sim = await FindSimResponseAsync(db, id, cancellationToken);

        return sim is null
            ? ApiProblems.NotFound($"SIM {id} was not found.")
            : TypedResults.Ok(sim);
    }

    private static async Task<IResult> CreateSim(
        SimRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var values = Validate(request, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var conflict = await FindUniquenessConflictAsync(db, values, excludeId: null, cancellationToken);

        if (conflict is not null)
        {
            return ApiProblems.Conflict(conflict);
        }

        var sim = new Sim
        {
            PhoneNumber = values.PhoneNumber,
            Iccid = values.Iccid,
            Operator = values.Operator,
            PlanType = values.PlanType,
            MonthlyCost = values.MonthlyCost,
            Status = SimStatuses.Available,
            CreatedAt = Timestamps.UtcNow()
        };

        db.Sims.Add(sim);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/sims/{sim.Id}", ToResponse(sim, currentAllocation: null));
    }

    private static async Task<IResult> UpdateSim(
        int id,
        SimRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var values = Validate(request, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var sim = await db.Sims.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (sim is null)
        {
            return ApiProblems.NotFound($"SIM {id} was not found.");
        }

        var conflict = await FindUniquenessConflictAsync(db, values, excludeId: id, cancellationToken);

        if (conflict is not null)
        {
            return ApiProblems.Conflict(conflict);
        }

        sim.PhoneNumber = values.PhoneNumber;
        sim.Iccid = values.Iccid;
        sim.Operator = values.Operator;
        sim.PlanType = values.PlanType;
        sim.MonthlyCost = values.MonthlyCost;

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await FindSimResponseAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> UpdateSimStatus(
        int id,
        SimStatusRequest request,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var status = request.Status?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(status))
        {
            return Invalid("status", "status is required.");
        }

        if (status == SimStatuses.Assigned)
        {
            return Invalid(
                "status",
                "status cannot be set to 'assigned' directly. Create an allocation instead.");
        }

        if (status is not (SimStatuses.Available or SimStatuses.Inactive))
        {
            return Invalid("status", "status must be 'available' or 'inactive'.");
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Lock the row so a concurrent allocation cannot slip in between
            // the active-allocation check and the status change.
            var sim = await LockSimAsync(db, id, cancellationToken);

            if (sim is null)
            {
                return ApiProblems.NotFound($"SIM {id} was not found.");
            }

            if (await db.Allocations.AnyAsync(
                    a => a.SimId == id && a.ReturnDate == null,
                    cancellationToken))
            {
                return ApiProblems.Conflict(
                    $"SIM {id} has an active allocation. Return it before changing the status.");
            }

            sim.Status = status;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return TypedResults.Ok(await FindSimResponseAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> DeleteSim(
        int id,
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var sim = await db.Sims.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (sim is null)
        {
            return ApiProblems.NotFound($"SIM {id} was not found.");
        }

        if (await db.Allocations.AnyAsync(a => a.SimId == id, cancellationToken))
        {
            return ApiProblems.Conflict($"SIM {id} has allocation history and cannot be deleted.");
        }

        if (await db.Alerts.AnyAsync(a => a.SimId == id, cancellationToken))
        {
            return ApiProblems.Conflict($"SIM {id} has alerts and cannot be deleted.");
        }

        if (await db.AuditLogs.AnyAsync(l => l.AffectedSimId == id, cancellationToken))
        {
            return ApiProblems.Conflict($"SIM {id} has audit log entries and cannot be deleted.");
        }

        db.Sims.Remove(sim);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetSimAllocations(
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

        if (!await db.Sims.AnyAsync(s => s.Id == id, cancellationToken))
        {
            return ApiProblems.NotFound($"SIM {id} was not found.");
        }

        var result = await AllocationEndpoints
            .QueryAllocations(db, simId: id, employeeId: null, active)
            .ToPagedResultAsync(paging, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static Task<SimResponse?> FindSimResponseAsync(
        SimGuardDbContext db,
        int id,
        CancellationToken cancellationToken) =>
        db.Sims
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(SimResponse.Projection)
            .FirstOrDefaultAsync(cancellationToken);

    private static ValidatedSim Validate(SimRequest request, ValidationErrors errors)
    {
        var phoneNumber = errors.RequiredPhoneNumber("phoneNumber", request.PhoneNumber);
        var iccid = errors.RequiredString("iccid", request.Iccid, IccidMaxLength);
        var simOperator = errors.RequiredString("operator", request.Operator, OperatorMaxLength);
        var planType = errors.RequiredString("planType", request.PlanType, PlanTypeMaxLength);

        if (request.MonthlyCost is null)
        {
            errors.Add("monthlyCost", "monthlyCost is required.");
        }
        else if (request.MonthlyCost < 0)
        {
            errors.Add("monthlyCost", "monthlyCost must be 0 or greater.");
        }
        else if (request.MonthlyCost > MonthlyCostMax)
        {
            errors.Add("monthlyCost", $"monthlyCost must be at most {MonthlyCostMax}.");
        }

        return new ValidatedSim(phoneNumber, iccid, simOperator, planType, request.MonthlyCost ?? 0);
    }

    private static async Task<string?> FindUniquenessConflictAsync(
        SimGuardDbContext db,
        ValidatedSim values,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (await db.Sims.AnyAsync(
                s => s.PhoneNumber == values.PhoneNumber && (excludeId == null || s.Id != excludeId),
                cancellationToken))
        {
            return $"A SIM with phone number '{values.PhoneNumber}' already exists.";
        }

        if (await db.Sims.AnyAsync(
                s => s.Iccid == values.Iccid && (excludeId == null || s.Id != excludeId),
                cancellationToken))
        {
            return $"A SIM with ICCID '{values.Iccid}' already exists.";
        }

        return null;
    }

    private static IResult Invalid(string field, string message)
    {
        var errors = new ValidationErrors();
        errors.Add(field, message);
        return errors.ToProblem();
    }

    private static SimResponse ToResponse(Sim sim, CurrentAllocationResponse? currentAllocation) =>
        new(
            sim.Id,
            sim.PhoneNumber,
            sim.Iccid,
            sim.Operator,
            sim.PlanType,
            sim.MonthlyCost,
            sim.Status,
            sim.CreatedAt,
            currentAllocation);

    private sealed record ValidatedSim(
        string PhoneNumber,
        string Iccid,
        string Operator,
        string PlanType,
        decimal MonthlyCost);
}
