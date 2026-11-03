using System.Linq.Expressions;
using SimGuard.Api.Models;

namespace SimGuard.Api.Dtos;

// Used by POST and PUT.
public record CarrierRequest(string? Name);

public record CarrierResponse(
    int Id,
    string Name,
    DateTime CreatedAt,
    int SimCount)
{
    public static readonly Expression<Func<Carrier, CarrierResponse>> Projection =
        c => new CarrierResponse(c.Id, c.Name, c.CreatedAt, c.Sims.Count());
}
