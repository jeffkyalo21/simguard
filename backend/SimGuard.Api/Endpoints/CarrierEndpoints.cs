using Microsoft.EntityFrameworkCore;
using Npgsql;
using SimGuard.Api.Common;
using SimGuard.Api.Common.Authorization;
using SimGuard.Api.Data;
using SimGuard.Api.Dtos;
using SimGuard.Api.Models;

namespace SimGuard.Api.Endpoints;

// Carriers (mobile network operators). Contract in
// backend/docs/api-carriers-usage.md.
public static class CarrierEndpoints
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 40;

    // Created by the AddCarriers migration on lower(name).
    private const string NameIndex = "idx_carriers_name_lower";

    public static IEndpointRouteBuilder MapCarrierEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/carriers")
            .WithTags("Carriers")
            .RequireAuthorization(AuthPolicies.OrgRead);

        group.MapGet("/", GetCarriers)
            .WithName("GetCarriers")
            .Produces<List<CarrierResponse>>();

        group.MapPost("/", CreateCarrier)
            .WithName("CreateCarrier")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<CarrierResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:int}", RenameCarrier)
            .WithName("RenameCarrier")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces<CarrierResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:int}", DeleteCarrier)
            .WithName("DeleteCarrier")
            .RequireAuthorization(AuthPolicies.OrgWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    // Every carrier, oldest first. Not paged: an organisation has a handful.
    private static async Task<IResult> GetCarriers(
        SimGuardDbContext db,
        CancellationToken cancellationToken)
    {
        var carriers = await db.Carriers
            .AsNoTracking()
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(CarrierResponse.Projection)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(carriers);
    }

    private static async Task<IResult> CreateCarrier(
        CarrierRequest request,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = ValidateName(request.Name, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        if (await NameExistsAsync(db, name, excludeId: null, cancellationToken))
        {
            return DuplicateName(name);
        }

        var carrier = new Carrier { Name = name, CreatedAt = Timestamps.UtcNow() };

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.Carriers.Add(carrier);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(AuditActions.CarrierCreated, null, null, Snapshot(carrier), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateName(ex))
        {
            // Another request added the same name between the check and the insert.
            return DuplicateName(name);
        }

        return TypedResults.Created(
            $"/api/carriers/{carrier.Id}",
            new CarrierResponse(carrier.Id, carrier.Name, carrier.CreatedAt, 0));
    }

    private static async Task<IResult> RenameCarrier(
        int id,
        CarrierRequest request,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = ValidateName(request.Name, errors);

        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        var carrier = await db.Carriers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (carrier is null)
        {
            return ApiProblems.NotFound($"Carrier {id} was not found.");
        }

        if (await NameExistsAsync(db, name, excludeId: id, cancellationToken))
        {
            return DuplicateName(name);
        }

        // Same name, same casing: nothing to change or audit.
        if (carrier.Name != name)
        {
            var before = Snapshot(carrier);
            carrier.Name = name;
            audit.Add(AuditActions.CarrierRenamed, null, before, Snapshot(carrier));

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsDuplicateName(ex))
            {
                return DuplicateName(name);
            }
        }

        var simCount = await db.Sims.CountAsync(s => s.CarrierId == id, cancellationToken);
        return TypedResults.Ok(new CarrierResponse(carrier.Id, carrier.Name, carrier.CreatedAt, simCount));
    }

    private static async Task<IResult> DeleteCarrier(
        int id,
        SimGuardDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var carrier = await db.Carriers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (carrier is null)
        {
            return ApiProblems.NotFound($"Carrier {id} was not found.");
        }

        var simCount = await db.Sims.CountAsync(s => s.CarrierId == id, cancellationToken);

        if (simCount > 0)
        {
            return InUse(carrier, simCount);
        }

        audit.Add(AuditActions.CarrierDeleted, null, Snapshot(carrier), null);
        db.Carriers.Remove(carrier);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // A SIM was added to the carrier after the count.
            return InUse(carrier, await db.Sims.CountAsync(s => s.CarrierId == id, cancellationToken));
        }

        return TypedResults.NoContent();
    }

    // Trimmed name, 2 to 40 characters.
    private static string ValidateName(string? value, ValidationErrors errors)
    {
        var name = errors.RequiredString("name", value, NameMaxLength);

        if (name.Length is > 0 and < NameMinLength)
        {
            errors.Add("name", $"name must be at least {NameMinLength} characters.");
        }

        return name;
    }

    // Matches the unique index on lower(name).
    private static Task<bool> NameExistsAsync(
        SimGuardDbContext db,
        string name,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        var key = name.ToLowerInvariant();

        return db.Carriers.AnyAsync(
            c => c.Name.ToLower() == key && (excludeId == null || c.Id != excludeId),
            cancellationToken);
    }

    private static bool IsDuplicateName(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == NameIndex;

    // Same ProblemDetails as other conflicts, plus the error on the name
    // field so a form can show it next to the input.
    private static IResult DuplicateName(string name)
    {
        var message = $"A carrier named '{name}' already exists.";

        return ApiProblems.Conflict(message, new Dictionary<string, object?>
        {
            ["errors"] = new Dictionary<string, string[]> { ["name"] = [message] }
        });
    }

    private static IResult InUse(Carrier carrier, int simCount) =>
        ApiProblems.Conflict(
            $"{simCount} {(simCount == 1 ? "SIM uses" : "SIMs use")} carrier '{carrier.Name}'. Move them to another carrier before removing it.",
            new Dictionary<string, object?> { ["simCount"] = simCount });

    private static object Snapshot(Carrier carrier) => new
    {
        id = carrier.Id,
        name = carrier.Name
    };
}
