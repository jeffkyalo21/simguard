using Microsoft.EntityFrameworkCore;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

public static class SimEndpoints
{
    private const int IccidMaxLength = 30;
    private const int PlanTypeMaxLength = 50;

    // numeric(10, 2)
    private const decimal MonthlyCostMax = 99_999_999.99m;

    public static IEndpointRouteBuilder MapSimEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/sims")
            .WithTags("SIMs")
            .RequireAuthorization(AuthPolicies.OrgRead);

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
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<SimResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:int}", UpdateSim)
            .WithName("UpdateSim")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<SimResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:int}/status", UpdateSimStatus)
            .WithName("UpdateSimStatus")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<SimResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:int}/tier", UpdateSimTier)
            .WithName("UpdateSimTier")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<SimTierResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:int}", DeleteSim)
            .WithName("DeleteSim")
            .RequireAuthorization(AuthPolicies.OrgWrite)
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
        int? carrierId,
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
                EF.Functions.ILike(s.Carrier.Name, operatorPattern, SearchPattern.EscapeCharacter));
        }

        if (carrierId is not null)
        {
            query = query.Where(s => s.CarrierId == carrierId);
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
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var values = Validate(request, errors);
        var carrier = await ResolveCarrierAsync(db, request, errors, cancellationToken);

        if (errors.HasErrors || carrier is null)
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
            CarrierId = carrier.Id,
            Carrier = carrier,
            PlanType = values.PlanType,
            MonthlyCost = values.MonthlyCost,
            Status = SimStatuses.Available,
            CreatedAt = Timestamps.UtcNow()
        };

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.Sims.Add(sim);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(AuditActions.SimCreated, sim.Id, null, Snapshot(sim), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return TypedResults.Created($"/api/sims/{sim.Id}", ToResponse(sim, currentAllocation: null));
    }

    private static async Task<IResult> UpdateSim(
        int id,
        SimRequest request,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var values = Validate(request, errors);
        var carrier = await ResolveCarrierAsync(db, request, errors, cancellationToken);

        if (errors.HasErrors || carrier is null)
        {
            return errors.ToProblem();
        }

        var sim = await db.Sims
            .Include(s => s.Carrier)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (sim is null)
        {
            return ApiProblems.NotFound($"SIM {id} was not found.");
        }

        var conflict = await FindUniquenessConflictAsync(db, values, excludeId: id, cancellationToken);

        if (conflict is not null)
        {
            return ApiProblems.Conflict(conflict);
        }

        var before = Snapshot(sim);

        sim.PhoneNumber = values.PhoneNumber;
        sim.Iccid = values.Iccid;
        sim.CarrierId = carrier.Id;
        sim.Carrier = carrier;
        sim.PlanType = values.PlanType;
        sim.MonthlyCost = values.MonthlyCost;

        audit.Add(AuditActions.SimUpdated, sim.Id, before, Snapshot(sim));
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await FindSimResponseAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> UpdateSimStatus(
        int id,
        SimStatusRequest request,
        SimGuardDbContext db,
        AuditWriter audit,
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

            var oldStatus = sim.Status;
            sim.Status = status;
            audit.Add(AuditActions.SimStatusChanged, sim.Id, new { status = oldStatus }, new { status });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return TypedResults.Ok(await FindSimResponseAsync(db, id, cancellationToken));
    }

    // The tier picks the ML cohort the SIM is compared with, so a change
    // affects every score computed after it (see ml_daily_usage).
    private static async Task<IResult> UpdateSimTier(
        int id,
        SimTierRequest request,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var tier = request.Tier?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(tier))
        {
            return Invalid("tier", "tier is required.");
        }

        if (!SimTiers.All.Contains(tier))
        {
            return Invalid("tier", $"tier must be one of: {string.Join(", ", SimTiers.All)}.");
        }

        string? oldTier;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var sim = await LockSimAsync(db, id, cancellationToken);

            if (sim is null)
            {
                return ApiProblems.NotFound($"SIM {id} was not found.");
            }

            oldTier = sim.Tier;

            if (oldTier != tier)
            {
                sim.Tier = tier;
                audit.Add(AuditActions.SimTierChanged, sim.Id, new { tier = oldTier }, new { tier });
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return TypedResults.Ok(new SimTierResponse(id, tier, oldTier));
    }

    private static async Task<IResult> DeleteSim(
        int id,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var sim = await db.Sims
            .Include(s => s.Carrier)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

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

        if (await db.RiskScores.AnyAsync(r => r.SimId == id, cancellationToken))
        {
            return ApiProblems.Conflict($"SIM {id} has risk scores and cannot be deleted.");
        }

        if (await db.DailyUsage.AnyAsync(u => u.SimId == id, cancellationToken))
        {
            return ApiProblems.Conflict($"SIM {id} has usage history and cannot be deleted.");
        }

        // Earlier audit rows keep their history: the database sets their
        // affected_sim_id to null. The deletion row carries the SIM in
        // oldValues because the SIM no longer exists.
        audit.Add(AuditActions.SimDeleted, null, Snapshot(sim), null);
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

        return new ValidatedSim(phoneNumber, iccid, planType, request.MonthlyCost ?? 0);
    }

    // The carrier by carrierId, or else by operator (the carrier name,
    // case-insensitive). Adds a 400 error and returns null when neither is
    // given or the carrier does not exist. The result is tracked.
    private static async Task<Carrier?> ResolveCarrierAsync(
        SimGuardDbContext db,
        SimRequest request,
        ValidationErrors errors,
        CancellationToken cancellationToken)
    {
        if (request.CarrierId is { } carrierId)
        {
            if (carrierId <= 0)
            {
                errors.Add("carrierId", "carrierId must be a positive integer.");
                return null;
            }

            var byId = await db.Carriers.FirstOrDefaultAsync(c => c.Id == carrierId, cancellationToken);

            if (byId is null)
            {
                errors.Add("carrierId", $"Carrier {carrierId} does not exist.");
            }

            return byId;
        }

        var name = request.Operator?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            errors.Add("carrierId", "carrierId is required (or operator, the carrier name).");
            return null;
        }

        var key = name.ToLowerInvariant();
        var byName = await db.Carriers.FirstOrDefaultAsync(c => c.Name.ToLower() == key, cancellationToken);

        if (byName is null)
        {
            errors.Add("operator", $"There is no carrier named '{name}'. Add it with POST /api/carriers first.");
        }

        return byName;
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

    private static object Snapshot(Sim sim) => new
    {
        id = sim.Id,
        phoneNumber = sim.PhoneNumber,
        iccid = sim.Iccid,
        carrierId = sim.CarrierId,
        @operator = sim.Carrier.Name,
        planType = sim.PlanType,
        monthlyCost = sim.MonthlyCost,
        status = sim.Status
    };

    private static SimResponse ToResponse(Sim sim, CurrentAllocationResponse? currentAllocation) =>
        new(
            sim.Id,
            sim.PhoneNumber,
            sim.Iccid,
            sim.CarrierId,
            sim.Carrier.Name,
            sim.PlanType,
            sim.MonthlyCost,
            sim.Status,
            sim.CreatedAt,
            currentAllocation);

    private sealed record ValidatedSim(
        string PhoneNumber,
        string Iccid,
        string PlanType,
        decimal MonthlyCost);
}
